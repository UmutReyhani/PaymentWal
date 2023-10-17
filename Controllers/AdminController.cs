using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using PaymentWall.Attributes;
using PaymentWall.Models;
using PaymentWall.Services;
using PaymentWall.User;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using static PaymentWall.Controllers.AdminController;
using static PaymentWall.Controllers.UserController;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly IConnectionService _connectionService;

        public AdminController(IConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        #region wrongpass

        private static MemoryCache _memCache = new MemoryCache(new MemoryCacheOptions());

        public static bool wrongPassword(string ip, bool upsert)
        {
            string cacheName = "wrongPassword-" + ip;
            int response = 0;

            _memCache.TryGetValue(cacheName, out response);

            if (upsert)
            {
                response += 1;
                var cacheEntryOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
                };

                _memCache.Set(cacheName, response, cacheEntryOptions);
            }

            return response > 7;
        }

        #endregion

        #region hashpass
        private string ComputeSha256Hash(string rawData)
        {
            using (System.Security.Cryptography.SHA256 sha256Hash = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawData));

                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
        #endregion

        #region xss checker
        private bool xssCheck(object data)
        {
            foreach (PropertyInfo pi in data.GetType().GetProperties())
            {
                var val = Convert.ToString(pi.GetValue(data, null));
                if (!string.IsNullOrEmpty(val) && (val.Contains("<") | val.Contains(">")))
                {
                    return true;
                }
            }
            return false;
        }
        #endregion

        #region CreateAdmin
        public class _createAdminReq
        {
            [Required]
            public string name { get; set; }
            [Required]
            [EmailAddress]
            public string email { get; set; }
            [Required]
            public string password { get; set; }
        }

        public class _createAdminRes
        {
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        public ActionResult<_createAdminRes> CreateAdmin([FromBody] _createAdminReq data)
        {
            if (xssCheck(data))
            {
                return Ok(new _createAdminRes { type = "error", message = " '<' or '>' characters not allowed." });
            }
            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");

            var existingAdminByEmail = _adminCollection.AsQueryable().FirstOrDefault(a => a.email == data.email);

            if (existingAdminByEmail != null)
            {
                return Ok(new _createAdminRes { type = "error", message = "An admin already exists with this email." });
            }

            Admin newAdmin = new Admin
            {
                name = data.name,
                email = data.email,
                password = ComputeSha256Hash(data.password),
                active = "1",
                lastLogin = DateTimeOffset.UtcNow,
                failedLoginAttempts = 0
            };

            _adminCollection.InsertOne(newAdmin);

            var adminIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers["User-Agent"].FirstOrDefault();
            AdminLog adminLog = new AdminLog
            {
                userId = newAdmin._id,
                date = DateTimeOffset.UtcNow,
                ip = adminIpAddress,
                userAgent = userAgent,
                type = "0",
                previousStatus = null,
                updatedStatus = "1",
                reason = "Admin registration",
                adminId = newAdmin._id  // Admin'in kendi oluşturduğu için adminId aynı
            };
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");
            _adminLogCollection.InsertOne(adminLog);

            return Ok(new _createAdminRes { type = "success", message = "Admin created successfully." });
        }
        #endregion

        #region Admin Login

        public class _adminLoginReq
        {
            [Required]
            public string email { get; set; }
            [Required]
            public string password { get; set; }
            [Required]
            public string captchaResponse { get; set; }
        }

        public class _adminLoginRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        public ActionResult<_adminLoginRes> AdminLogin([FromBody] _adminLoginReq loginData)
        {
            string adminIpAddress = GetUserIpAddress();

            if (IsIpBlockedFromLogin(adminIpAddress))
            {
                return Ok(new _adminLoginRes { type = "error", message = "Your login was disabled 5 minutes." });
            }

            var correctCaptchaAnswer = HttpContext.Session.GetString("CaptchaAnswer");
            if (loginData.captchaResponse != correctCaptchaAnswer)
            {
                return Ok(new _adminLoginRes { type = "error", message = "Invalid captcha response." });
            }

            if (loginData == null || string.IsNullOrEmpty(loginData.email) || string.IsNullOrEmpty(loginData.password))
            {
                return Ok(new _adminLoginRes { type = "error", message = "mail cant be null" });
            }

            var adminInDb = GetAdminFromDb(loginData.email);
            if (adminInDb == null)
            {
                return Ok(new _adminLoginRes { type = "error", message = "Admin not found." });
            }

            var siteSettings = GetSiteSettings() ?? new Site { maxFailedLoginAttempts = 5 };

            if (adminInDb.active == "0")
            {
                return Ok(new _adminLoginRes { type = "error", message = "Your account is inactive." });
            }


            if (adminInDb.password != ComputeSha256Hash(loginData.password))
            {
                HandleFailedAdminLogin(adminInDb, adminIpAddress);
                HttpContext.Session.Remove("CaptchaAnswer");
                return Ok(new _adminLoginRes { type = "error", message = "Wrong Password." });
            }

            HandleSuccessfulAdminLogin(adminInDb);
            HttpContext.Session.Remove("CaptchaAnswer");
            return Ok(new _adminLoginRes { type = "success", message = "Login Successfull" });
        }
        private string GetUserIpAddress()
        {
            try
            {
                return HttpContext.Request.Headers.ContainsKey("CF-CONNECTING-IP")
                    ? HttpContext.Request.Headers["CF-CONNECTING-IP"].ToString()
                    : HttpContext.Connection.RemoteIpAddress.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
        private Site GetSiteSettings()
        {
            var _siteCollection = _connectionService.db().GetCollection<Site>("Site");
            return _siteCollection.AsQueryable().FirstOrDefault();
        }

        private bool IsIpBlockedFromLogin(string ip)
        {
            return wrongPassword(ip, false);
        }

        private Admin GetAdminFromDb(string email)
        {
            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");
            return _adminCollection.AsQueryable().FirstOrDefault(a => a.email == email);
        }

        private void HandleFailedAdminLogin(Admin admin, string ip)
        {
            // Admin için hatalı oturum açma işlemini işle
            // Örneğin, hatalı giriş sayısını artırabilir veya IP adresini engelleyebilirsiniz.
            // Bu işlevsellik için 'HandleFailedLogin' fonksiyonundan yararlanabilirsiniz.
            // Aşağıdaki kodu gerektiği şekilde uyarlamalısınız.

            wrongPassword(ip, true);

            admin.failedLoginAttempts += 1; // Eğer Admin modelinizde failedLoginAttempts gibi bir özellik yoksa, bu satırı düzenlemelisiniz.
                                            // Eğer belirli bir sayıda hatalı girişten sonra admini engellemek istiyorsanız bu kısmı uygulayabilirsiniz.

            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");
            var update = Builders<Admin>.Update
                .Set(a => a.failedLoginAttempts, admin.failedLoginAttempts);

            _adminCollection.UpdateOne(a => a._id == admin._id, update);
        }

        private void HandleSuccessfulAdminLogin(Admin admin)
        {
            var userAgent = HttpContext.Request.Headers["User-Agent"].FirstOrDefault();

            AdminLog adminLog = new AdminLog
            {
                userId = admin._id,
                date = DateTimeOffset.UtcNow,
                ip = GetUserIpAddress(),
                userAgent = userAgent,
                type = "1"
            };

            var _logCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

            _logCollection.InsertOne(adminLog);

            admin.lastLogin = DateTimeOffset.UtcNow;

            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");

            var update = Builders<Admin>.Update
                .Set(a => a.lastLogin, admin.lastLogin);

            _adminCollection.UpdateOne(a => a._id == admin._id, update);

            userFunctions.SetCurrentAdminToSession(HttpContext, admin);
        }

        #endregion

        #region Logout Admin
        public class _adminLogoutRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        public ActionResult<_logoutRes> AdminLogout()
        {
            var userId = HttpContext.Session.GetString("id");

            LogAdminAction(userId, "2");

            userFunctions.ClearCurrentUserFromSession(HttpContext);
            return Ok(new _logoutRes { type = "success", message = "Admin logged out successfully." });
        }
        private void LogAdminAction(string userId, string actionType)
        {
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

            var adminLog = new AdminLog
            {
                userId = ObjectId.Parse(userId),
                date = DateTimeOffset.Now,
                userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                ip = HttpContext.Connection.RemoteIpAddress.ToString(),
                type = actionType
            };

            _adminLogCollection.InsertOne(adminLog);
        }

        #endregion

        #region Update User Status

        public class _updateUserStatusReq
        {
            [Required]
            public string userId { get; set; }  // Güncellenecek kullanıcının ID'si
            [Required]
            public string status { get; set; }  // 0: passive, 1: active 2: banned
            public string description { get; set; }  // Açıklama veya sebep

        }

        public class _updateUserStatusRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]"), CheckAdminLogin]
        public async Task<ActionResult<_updateUserStatusRes>> UpdateUserStatus([FromBody] _updateUserStatusReq req)
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

            var existingUser = _userCollection.AsQueryable().FirstOrDefault(u => u._id.ToString() == req.userId);
            if (existingUser == null)
            {
                return Ok(new _updateUserStatusRes { type = "error", message = "User not found." });
            }

            var adminIdFromSession = HttpContext.Session.GetString("id");
            ObjectId adminObjectId = ObjectId.Parse(adminIdFromSession);

            var statusUpdate = Builders<Users>.Update
                .Set(u => u.status, req.status);

            var adminLog = new AdminLog
            {
                userId = existingUser._id,
                adminId = adminObjectId,
                previousStatus = existingUser.status,
                updatedStatus = req.status,
                date = DateTimeOffset.Now,
                type = "status update",
                userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                ip = HttpContext.Connection.RemoteIpAddress.ToString()
            };
            await _adminLogCollection.InsertOneAsync(adminLog);

            await _userCollection.UpdateOneAsync(u => u._id == existingUser._id, statusUpdate);

            return Ok(new _updateUserStatusRes { type = "success", message = "User status updated successfully." });
        }
        #endregion

        #region Transfer Check
        public class transferFilterRequest
        {
            public DateTime? startDate { get; set; }
            public DateTime? endDate { get; set; }
            public string userId { get; set; }
            public int? page { get; set; }
            private int _pageSize = 10;
            public int pageSize
            {
                get => _pageSize;
                set
                {
                    _pageSize = (value > 50) ? 50 : value;
                }
            }
        }

        public class transferListResponse
        {
            public string type { get; set; }
            public string message { get; set; }
            public List<Accounting> transfers { get; set; }
        }

        [HttpGet("[action]"), CheckAdminLogin]
        public ActionResult<transferListResponse> GetAllTransfers([FromQuery] transferFilterRequest filter)
        {
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");
            var query = (IMongoQueryable<Accounting>)_accountingCollection.AsQueryable();

            if (filter.startDate.HasValue)
            {
                query = query.Where(a => a.date >= filter.startDate.Value);
            }

            if (filter.endDate.HasValue)
            {
                query = query.Where(a => a.date <= filter.endDate.Value);
            }

            if (!string.IsNullOrEmpty(filter.userId))
            {
                ObjectId userIdObj = ObjectId.Parse(filter.userId);
                query = query.Where(a => a.userId == userIdObj);
            }

            if (filter.page.HasValue)
            {
                int skipAmount = (filter.page.Value - 1) * filter.pageSize;
                query = query.Skip(skipAmount).Take(filter.pageSize);
            }

            var transfers = query.ToList();

            var response = new transferListResponse
            {
                type = "success",
                message = "Transfers retrieved successfully.",
                transfers = transfers
            };

            return Ok(response);
        }
        #endregion

    }
}
