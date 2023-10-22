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
using System.Data;
using System.Reflection;
using static PaymentWall.Controllers.AccountingController;
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
            [Required]
            public int role { get; set; } // Eklenen role alanı
        }

        public class _createAdminRes
        {
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
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
                role = data.role, // Yeni eklenen role alanı
                active = 1,
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
                type = 0,
                previousStatus = null,
                updatedStatus = 1,
                reason = "Admin or Person Registration",
                role = data.role,
                adminId = newAdmin._id
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

            if (adminInDb.active == 0)
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
                type = 1
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

            LogAdminAction(userId, 2);

            userFunctions.ClearCurrentAdminFromSession(HttpContext);
            return Ok(new _logoutRes { type = "success", message = "Admin logged out successfully." });
        }
        private void LogAdminAction(string userId, int actionType)
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

        #region Delete Admin

        public class _deleteAdminReq
        {
            [Required]
            public string adminId { get; set; }
        }

        public class _deleteAdminRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
        public async Task<ActionResult<_deleteAdminRes>> DeleteAdmin([FromBody] _deleteAdminReq req)
        {
            var adminIdToDelete = ObjectId.Parse(req.adminId);

            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");

            var adminToDelete = _adminCollection.AsQueryable().FirstOrDefault(admin => admin._id == adminIdToDelete);

            if (adminToDelete == null)
            {
                return Ok(new _deleteAdminRes { type = "error", message = "Admin not found." });
            }

            await LogAdminDeletion(adminIdToDelete.ToString(), HttpContext.Session.GetString("id"));

            _adminCollection.DeleteOne(admin => admin._id == adminIdToDelete);

            return Ok(new _deleteAdminRes { type = "success", message = "Admin deleted successfully." });

        }

        private async Task LogAdminDeletion(string deletedAdminId, string adminId)
        {
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

            var adminLog = new AdminLog
            {
                userId = ObjectId.Parse(deletedAdminId),
                adminId = ObjectId.Parse(adminId),
                date = DateTimeOffset.Now,
                type = 4,
                userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                ip = HttpContext.Connection.RemoteIpAddress.ToString(),
                reason = "Admin deleted"
            };

            await _adminLogCollection.InsertOneAsync(adminLog);
        }

        #endregion

        #region Update Admin Password

        public class _updateAdminPasswordReq
        {
            [Required]
            public string oldPassword { get; set; }

            [Required]
            public string newPassword { get; set; }
        }

        public class _updateAdminPasswordRes
        {
            [Required]
            public string type { get; set; }

            public string message { get; set; }
        }

        [HttpPost("[action]"), CheckAdminLogin]
        public async Task<ActionResult<_updateAdminPasswordRes>> UpdateAdminPassword([FromBody] _updateAdminPasswordReq req)
        {
            var adminIdFromSession = HttpContext.Session.GetString("adminId");
            if (string.IsNullOrEmpty(adminIdFromSession))
            {
                return Ok(new _updateAdminPasswordRes { type = "error", message = "Admin not logged in." });
            }

            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admins");

            var existingAdmin = _adminCollection.AsQueryable().FirstOrDefault(a => a._id.ToString() == adminIdFromSession);
            if (existingAdmin == null)
            {
                return Ok(new _updateAdminPasswordRes { type = "error", message = "Admin not found." });
            }

            if (ComputeSha256Hash(req.oldPassword) != existingAdmin.password)
            {
                return Ok(new _updateAdminPasswordRes { type = "error", message = "Incorrect old password." });
            }

            var passwordUpdate = Builders<Admin>.Update.Set(a => a.password, ComputeSha256Hash(req.newPassword));
            await _adminCollection.UpdateOneAsync(a => a._id == existingAdmin._id, passwordUpdate);

            return Ok(new _updateAdminPasswordRes { type = "success", message = "Password updated successfully." });
        }

        #endregion

        #region Get All Admins
        public class adminFilterReq
        {
            public int? role { get; set; }
            public int? active { get; set; }
        }

        public class _getAllAdminsRes
        {
            public string type { get; set; }
            public string message { get; set; }
            public List<Admin> admins { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
        public ActionResult<_getAllAdminsRes> GetAllAdmins([FromBody] adminFilterReq filter)
        {
            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");

            var query = _adminCollection.AsQueryable();

            if (filter != null)
            {
                if (filter.role.HasValue)
                {
                    query = query.Where(a => a.role == filter.role.Value);
                }

                if (filter.active.HasValue)
                {
                    query = query.Where(a => a.active == filter.active.Value);
                }
            }

            var adminList = query.ToList();

            if (adminList == null || adminList.Count == 0)
            {
                return Ok(new _getAllAdminsRes { type = "error", message = "No admins found." });
            }

            foreach (var admin in adminList)
            {
                admin.password = null;
            }

            return Ok(new _getAllAdminsRes { type = "success", message = "Admins retrieved successfully.", admins = adminList });
        }

        #endregion

        #region Update Admin Status

        public class _updateAdminStatusReq
        {
            [Required]
            public string adminId { get; set; }
            [Required]
            public int status { get; set; }
            public string description { get; set; }
        }

        public class _updateAdminStatusRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        public async Task<ActionResult<_updateAdminStatusRes>> UpdateAdminStatus([FromBody] _updateAdminStatusReq req)
        {
            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

            var existingAdmin = _adminCollection.AsQueryable().FirstOrDefault(a => a._id.ToString() == req.adminId);
            if (existingAdmin == null)
            {
                return Ok(new _updateAdminStatusRes { type = "error", message = "Admin not found." });
            }

            var adminIdFromSession = HttpContext.Session.GetString("id");
            ObjectId adminObjectId = ObjectId.Parse(adminIdFromSession);

            var statusUpdate = Builders<Admin>.Update
                .Set(a => a.active, req.status);

            var adminLog = new AdminLog
            {
                userId = existingAdmin._id,
                adminId = adminObjectId,
                previousStatus = existingAdmin.active,
                updatedStatus = req.status,
                date = DateTimeOffset.Now,
                type = 3,
                userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                ip = HttpContext.Connection.RemoteIpAddress.ToString()
            };
            await _adminLogCollection.InsertOneAsync(adminLog);

            await _adminCollection.UpdateOneAsync(a => a._id == existingAdmin._id, statusUpdate);

            return Ok(new _updateAdminStatusRes { type = "success", message = "Admin status updated successfully." });
        }


        #endregion

        #region Update User Status

        public class _updateUserStatusReq
        {
            [Required]
            public string userId { get; set; }  // Güncellenecek kullanıcının ID'si
            [Required]
            public int status { get; set; }  // 0: passive, 1: active 2: banned
            public string description { get; set; }  // Açıklama veya sebep

        }

        public class _updateUserStatusRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
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
                type = 3,
                userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                ip = HttpContext.Connection.RemoteIpAddress.ToString()
            };
            await _adminLogCollection.InsertOneAsync(adminLog);

            await _userCollection.UpdateOneAsync(u => u._id == existingUser._id, statusUpdate);

            return Ok(new _updateUserStatusRes { type = "success", message = "User status updated successfully." });
        }
        #endregion

        #region Get All Users

        public class GetAllUsersReq
        {
            public DateTime? startDate { get; set; }
            public DateTime? endDate { get; set; }
            public string? searchQuery { get; set; }
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

        public class GetAllUsersRes
        {
            [Required]
            public string type { get; set; }
            [Required]
            public string message { get; set; }
            public List<UserDTO> users { get; set; }
            public int totalUsersCount { get; set; }
        }

        public class UserDTO
        {
            public string _id { get; set; }
            public string name { get; set; }
            public string surname { get; set; }
            public DateTime birthDate { get; set; }
            public DateTimeOffset register { get; set; }
            public DateTimeOffset lastLogin { get; set; }
            public string email { get; set; }
            public int type { get; set; }
            public int status { get; set; }
        }

        [HttpGet("[action]")]
        [CheckAdminLogin(0, 1)]
        public ActionResult<GetAllUsersRes> GetAllUsers([FromQuery] GetAllUsersReq req)
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            IQueryable<Users> queryableUsers = _userCollection.AsQueryable();

            if (req.startDate.HasValue && req.endDate.HasValue)
            {
                queryableUsers = queryableUsers.Where(u => u.register >= req.startDate.Value && u.register <= req.endDate.Value);
            }

            if (!string.IsNullOrEmpty(req.searchQuery))
            {
                queryableUsers = queryableUsers.Where(u => u.name.Contains(req.searchQuery) ||
                                                          u.surname.Contains(req.searchQuery) ||
                                                          u.email.Contains(req.searchQuery));
            }

            int totalUsersCount = queryableUsers.Count();

            int currentPage = req.page.HasValue && req.page > 0 ? req.page.Value : 1;

            if (currentPage <= 0 || req.pageSize <= 0)
            {
                return Ok(new GetAllUsersRes { type = "false", message = "Page and pageSize must be provided and greater than 0." });
            }

            var users = queryableUsers
                .Skip((currentPage - 1) * req.pageSize)
                .Take(req.pageSize)
                .Select(u => new UserDTO
                {
                    _id = u._id.ToString(),
                    name = u.name,
                    surname = u.surname,
                    birthDate = u.birthDate,
                    register = u.register,
                    lastLogin = u.lastLogin,
                    email = u.email,
                    type = u.type,
                    status = u.status
                })
                .ToList();

            return Ok(new GetAllUsersRes
            {
                type = "success",
                message = "Users fetched successfully",
                users = users,
                totalUsersCount = totalUsersCount
            });
        }

        #endregion

        #region financial reports

        public class _adminFinancialReportRequest
        {
            [Required]
            public string email { get; set; }
            public DateTime? startDate { get; set; }
            public DateTime? endDate { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }

        public class _adminFinancialReportResponse
        {
            public string type { get; set; }
            public string message { get; set; }
            public decimal totalIncome { get; set; }
            public decimal totalExpense { get; set; }
            public decimal netBalance { get; set; }
        }

        [HttpPost("[action]"), CheckAdminLogin]
        public ActionResult<_adminFinancialReportResponse> GetSpecificUserFinancialReport([FromBody] _adminFinancialReportRequest request)
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");

            if (string.IsNullOrEmpty(request.email))
            {
                return Ok(new _adminFinancialReportResponse { type = "error", message = "Email is required." });
            }

            var user = _userCollection.AsQueryable().FirstOrDefault(u => u.email == request.email);

            if (user == null)
            {
                return Ok(new _adminFinancialReportResponse { type = "error", message = "User with the provided email does not exist." });
            }

            var userWallets = _walletCollection.AsQueryable().Where(wallet => wallet.userId == user._id).ToList();

            decimal totalIncome = 0;
            decimal totalExpense = 0;
            decimal netBalance = 0;

            if (!request.pageNumber.HasValue)
            {
                request.pageNumber = 1;
            }
            if (!request.pageSize.HasValue)
            {
                request.pageSize = 10;
            }

            var skip = (request.pageNumber.Value - 1) * request.pageSize.Value;

            foreach (var wallet in userWallets)
            {
                var query = _accountingCollection.AsQueryable().Where(a => a.walletId == wallet._id);

                if (request.startDate.HasValue)
                {
                    query = query.Where(a => a.date >= request.startDate.Value);
                }

                if (request.endDate.HasValue)
                {
                    query = query.Where(a => a.date <= request.endDate.Value);
                }

                var accountingForWallet = query.Skip(skip).Take(request.pageSize.Value).ToList();

                foreach (var entry in accountingForWallet)
                {
                    if (entry.amount > 0) totalIncome += entry.amount;
                    else totalExpense += entry.amount;
                }
                netBalance += wallet.balance;
            }

            return Ok(new _adminFinancialReportResponse
            {
                type = "success",
                message = $"Financial report for user {user.email} fetched successfully.",
                totalIncome = totalIncome,
                totalExpense = Math.Abs(totalExpense),
                netBalance = netBalance
            });
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

        [HttpGet("[action]")]
        [CheckAdminLogin(0,1)]
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

        #region Delete Wallet

        public class _deleteWalletReq
        {
            [Required]
            public string userId { get; set; }
            [Required]
            public int walletId { get; set; }
        }

        public class _deleteWalletRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(0, 1)]
        public ActionResult<_deleteWalletRes> DeleteWallet([FromBody] _deleteWalletReq req)
        {
            var userId = ObjectId.Parse(req.userId);
            var walletIdToDelete = req.walletId;

            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");

            var userWallets = _walletCollection.AsQueryable().Where(wallet => wallet.userId == userId).ToList();

            var walletToDelete = userWallets.FirstOrDefault(wallet => wallet._id == walletIdToDelete);

            if (walletToDelete == null)
            {
                return Ok(new _deleteWalletRes { type = "error", message = "Wallet not found for the specified user." });
            }

            // 2. Wallet'i silme işlemini gerçekleştirin
            _walletCollection.DeleteOne(wallet => wallet._id == walletIdToDelete);

            return Ok(new _deleteWalletRes { type = "success", message = "Wallet deleted successfully." });
        }
        #endregion

        #region Create Limit

        public class _setLimitReq
        {
            public string userId { get; set; }
            public decimal maxDeposit { get; set; }
            public decimal minDeposit { get; set; }
            public decimal dailyMaxDeposit { get; set; }
            public decimal monthlyMaxDeposit { get; set; }
        }

        public class _limitRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        public ActionResult<_limitRes> AddLimit([FromBody] _setLimitReq req)
        {
            var _limitCollection = _connectionService.db().GetCollection<Limit>("Limit");
            var newLimit = new Limit
            {
                maxDeposit = req.maxDeposit,
                minDeposit = req.minDeposit,
                dailyMaxDeposit = req.dailyMaxDeposit,
                monthlyMaxDeposit = req.monthlyMaxDeposit
            };
            _limitCollection.InsertOne(newLimit);
            return Ok(new _limitRes { type = "success", message = "Limit added successfully." });
        }

        #endregion

        #region Update Limit

        public class _updateLimitReq : _setLimitReq
        {
            [Required]
            public string limitId { get; set; } // Güncellenecek limitin ID'si
        }

        [HttpPost("[action]")]
        public ActionResult<_limitRes> UpdateLimit([FromBody] _updateLimitReq req)
        {
            var _limitCollection = _connectionService.db().GetCollection<Limit>("Limit");
            var update = Builders<Limit>.Update
                .Set(l => l.maxDeposit, req.maxDeposit)
                .Set(l => l.minDeposit, req.minDeposit)
                .Set(l => l.dailyMaxDeposit, req.dailyMaxDeposit)
                .Set(l => l.monthlyMaxDeposit, req.monthlyMaxDeposit);
            _limitCollection.UpdateOne(l => l._id == ObjectId.Parse(req.limitId), update);
            return Ok(new _limitRes { type = "success", message = "Limit updated successfully." });
        }

        #endregion


    }
}
