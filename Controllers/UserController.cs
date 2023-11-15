using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.ComponentModel.DataAnnotations;
using PaymentWall.Models;
using PaymentWall.Services;
using PaymentWall.User;
using System.Net.Mail;
using Microsoft.Extensions.Caching.Memory;
using PaymentWall.Attributes;
using System.Reflection;
using MongoDB.Bson;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {

        private readonly IStringLocalizer _localizer;

        private readonly IConnectionService _connectionService;
        public UserController(IConnectionService connectionService, IStringLocalizer<UserController> localizer)
        {
            _connectionService = connectionService;
            _localizer = localizer;
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

        #region uniqwallet
        private int GenerateUniqueWalletId()
        {
            Random random = new Random();
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            int walletId;
            bool isUnique = false;

            do
            {
                walletId = random.Next(10000000, 99999999);
                var existingWallet = _walletCollection.AsQueryable().FirstOrDefault(w => w._id == walletId);
                if (existingWallet == null)
                {
                    isUnique = true;
                }
            } while (!isUnique);

            return walletId;
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

        #region CreateUser

        public class _createUserReq
        {
            [Required]
            public string name { get; set; }
            [Required]
            public string surname { get; set; }
            [Required]
            public DateTime birthDate { get; set; } // 18 den küçük olamaz
            [Required]
            public string email { get; set; }
            [Required]
            public string password { get; set; }
            [Required]
            public string currency { get; set; } //ucu açık
            [Required]
            public int type { get; set; } // personel-business(0-1)
            [Required]
            public string address { get; set; }
            [Required]
            public string city { get; set; }
            [Required]
            public string postCode { get; set; }
            [Required]
            public string country { get; set; }
            [Required]
            public string phoneNumber { get; set; }
        }

        public class _createUserRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        public ActionResult<_createUserRes> CreateUser([FromBody] _createUserReq data)
        {
            if (xssCheck(data))
            {
                return Ok(new _createUserRes { type = "error", message = _localizer["12"].Value });
            }
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _addressCollection = _connectionService.db().GetCollection<Address>("Address");
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");

            DateTime today = DateTime.Today;
            int age = today.Year - data.birthDate.Year;

            if (age < 18)
            {
                return Ok(new _createUserRes { type = "error", message = _localizer["mustBeAtLeast18"].Value });
            }

            var existingUserByEmail = _userCollection.AsQueryable().FirstOrDefault(u => u.email == data.email);

            if (existingUserByEmail != null)
            {
                return Ok(new _createUserRes { type = "error", message = _localizer["userAlreadyExists"].Value });
            }

            if (data.type != 0 && data.type != 1)
            {
                return Ok(new _createUserRes { type = "error", message = _localizer["typeCanOnlyBe"].Value });
            }

            Users newUser = new Users
            {
                name = data.name,
                surname = data.surname,
                birthDate = data.birthDate,
                email = data.email,
                password = ComputeSha256Hash(data.password),
                type = data.type,
                status = 1,
                verified = false,
                emailVerified = false,
                register = DateTimeOffset.UtcNow
            };

            _userCollection.InsertOne(newUser);

            Wallet newWallet = new Wallet
            {
                _id = GenerateUniqueWalletId(),
                userId = newUser._id,
                balance = 0,
                currency = data.currency,
            };

            _walletCollection.InsertOne(newWallet);

            Address userAddress = new Address
            {
                userId = newUser._id,
                address = data.address,
                city = data.city,
                postCode = data.postCode,
                country = data.country,
                phoneNumber = data.phoneNumber
            };
            _addressCollection.InsertOne(userAddress);


            var userIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers["User-Agent"].FirstOrDefault();
            Log userLog = new Log
            {
                userId = newUser._id,
                date = DateTimeOffset.UtcNow,
                ip = userIpAddress,
                userAgent = userAgent,
                type = 0
            };
            var _logCollection = _connectionService.db().GetCollection<Log>("Log");
            _logCollection.InsertOne(userLog);


            return Ok(new _createUserRes { type = "success", message = _localizer["userCreatedSuccessfully"].Value });

        }
        #endregion

        #region login user
        public class _loginReq
        {
            [Required]
            public string email { get; set; }
            [Required]
            public string password { get; set; }
            [Required]
            public string captchaResponse { get; set; }
        }

        public class _loginRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }
        [HttpPost("[action]")]
        public ActionResult<_loginRes> Login([FromBody] _loginReq loginData)
        {
            string userIpAddress = GetUserIpAddress();

            if (IsIpBlockedFromLogin(userIpAddress))
            {

                return Ok(new _loginRes { type = "error", message = _localizer["loginDisabled"].Value });
            }

            var correctCaptchaAnswer = HttpContext.Session.GetString("CaptchaAnswer");
            if (loginData.captchaResponse != correctCaptchaAnswer)
            {
                return Ok(new _loginRes { type = "error", message = _localizer["invalidCaptcha"].Value });
            }

            if (loginData == null || string.IsNullOrEmpty(loginData.email) || string.IsNullOrEmpty(loginData.password))
            {
                return Ok(new _loginRes { type = "error", message = _localizer["18"].Value });
            }

            var userInDb = GetUserFromDb(loginData.email);
            if (userInDb == null)
            {
                return Ok(new _loginRes { type = "error", message = _localizer["43"].Value });
            }

            var siteSettings = GetSiteSettings() ?? new Site { maxFailedLoginAttempts = 5 };

            if (userInDb.status == 0)
            {
                return Ok(new _loginRes { type = "error", message = _localizer["accountInactive"].Value });
            }

            if (userInDb.password != ComputeSha256Hash(loginData.password))
            {
                HandleFailedLogin(userInDb, userIpAddress, siteSettings);
                HttpContext.Session.Remove("CaptchaAnswer");
                return Ok(new _loginRes { type = "error", message = _localizer["wrongPassword"].Value });
            }

            HandleSuccessfulLogin(userInDb);
            HttpContext.Session.Remove("CaptchaAnswer");
            return Ok(new _loginRes { type = "success", message = _localizer["loginSuccessful"].Value });
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

        private bool IsIpBlockedFromLogin(string ip)
        {
            return wrongPassword(ip, false);
        }

        private Users GetUserFromDb(string email)
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            return _userCollection.AsQueryable().FirstOrDefault(u => u.email == email);
        }

        private Site GetSiteSettings()
        {
            var _siteCollection = _connectionService.db().GetCollection<Site>("Site");
            return _siteCollection.AsQueryable().FirstOrDefault();
        }

        private void HandleFailedLogin(Users user, string ip, Site siteSettings)
        {
            wrongPassword(ip, true);

            user.failedLoginAttempts += 1;
            if (user.failedLoginAttempts >= siteSettings.maxFailedLoginAttempts)
            {
                user.status = 0;
            }

            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var update = Builders<Users>.Update
                .Set(u => u.failedLoginAttempts, user.failedLoginAttempts)
                .Set(u => u.status, user.status);
            _userCollection.UpdateOne(u => u._id == user._id, update);

        }

        private void HandleSuccessfulLogin(Users user)
        {
            user.failedLoginAttempts = 0;

            var userAgent = HttpContext.Request.Headers["User-Agent"].FirstOrDefault();
            Log userLog = new Log
            {
                userId = user._id,
                date = DateTimeOffset.UtcNow,
                ip = GetUserIpAddress(),
                userAgent = userAgent,
                type = 1
            };

            var _logCollection = _connectionService.db().GetCollection<Log>("Log");
            _logCollection.InsertOne(userLog);

            user.lastLogin = DateTimeOffset.UtcNow;

            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var update = Builders<Users>.Update
                .Set(u => u.failedLoginAttempts, user.failedLoginAttempts)
                .Set(u => u.lastLogin, user.lastLogin);

            _userCollection.UpdateOne(u => u._id == user._id, update);

            userFunctions.SetCurrentUserToSession(HttpContext, user);
        }



        #endregion

        #region Update User

        public class _updateUserReq
        {
            public string oldPassword { get; set; }
            public string newPassword { get; set; }
            public string address { get; set; }
            public string city { get; set; }
            public string postCode { get; set; }
            public string phoneNumber { get; set; }
        }

        public class _updateUserRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]"), CheckUserLogin]
        public async Task<ActionResult<_updateUserRes>> updateUser([FromBody] _updateUserReq req)
        {
            if (!config.avaibleCurrencies.Contains("USD"))
            {
                return Ok(req);
            }
            var userIdFromSession = HttpContext.Session.GetString("id");
            if (string.IsNullOrEmpty(userIdFromSession))
            {
                return Ok(new _updateUserRes { type = "error", message = _localizer["userNotLoggedIn"].Value });
            }

            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _addressCollection = _connectionService.db().GetCollection<Address>("Address");

            var existingUser = _userCollection.AsQueryable().FirstOrDefault(u => u._id.ToString() == userIdFromSession);
            if (existingUser == null)
            {
                return Ok(new _updateUserRes { type = "error", message = _localizer["43"].Value });
            }

            if (!string.IsNullOrEmpty(req.newPassword))
            {
                if (ComputeSha256Hash(req.oldPassword) != existingUser.password)
                {
                    return Ok(new _updateUserRes { type = "error", message = _localizer["incorrectOldPassword"].Value });
                }
                var passwordUpdate = Builders<Users>.Update.Set(u => u.password, ComputeSha256Hash(req.newPassword));
                await _userCollection.UpdateOneAsync(u => u._id == existingUser._id, passwordUpdate);
            }

            var existingAddress = _addressCollection.AsQueryable().FirstOrDefault(a => a.userId == existingUser._id);
            if (existingAddress != null)
            {
                var addressUpdate = Builders<Address>.Update
                    .Set(a => a.address, req.address ?? existingAddress.address)
                    .Set(a => a.city, req.city ?? existingAddress.city)
                    .Set(a => a.postCode, req.postCode ?? existingAddress.postCode)
                    .Set(a => a.phoneNumber, req.phoneNumber ?? existingAddress.phoneNumber);

                await _addressCollection.UpdateOneAsync(a => a._id == existingAddress._id, addressUpdate);
            }
            else
            {
                Address newAddress = new Address
                {
                    userId = existingUser._id,
                    address = req.address,
                    city = req.city,
                    postCode = req.postCode,
                    phoneNumber = req.phoneNumber
                };
                await _addressCollection.InsertOneAsync(newAddress);
            }

            return Ok(new _updateUserRes { type = "success", message = _localizer["userUpdatedSuccessfully"].Value });
        }


        #endregion

        #region Logout User
        public class _logoutRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]"), CheckUserLogin]
        public ActionResult<_logoutRes> Logout()
        {
            var userId = HttpContext.Session.GetString("id");

            LogUserAction(userId, 2);

            userFunctions.ClearCurrentUserFromSession(HttpContext);
            return Ok(new _logoutRes { type = "success", message = _localizer["loggedOutSuccessfully"].Value });
        }

        private void LogUserAction(string userId, int actionType)
        {
            var _logCollection = _connectionService.db().GetCollection<Log>("Log");

            var log = new Log
            {
                userId = ObjectId.Parse(userId),
                date = DateTimeOffset.Now,
                userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                ip = HttpContext.Connection.RemoteIpAddress.ToString(),
                type = actionType
            };

            _logCollection.InsertOne(log);
        }


        #endregion

        //#region forgot pass
        //public class _forgotPasswordRequest
        //{
        //    [Required]
        //    public string email { get; set; }
        //}

        //public class _forgotPasswordResponse
        //{
        //    public string type { get; set; }
        //    public string message { get; set; }
        //}

        //[HttpPost("forgotpassword")]
        //public ActionResult RequestPasswordReset([FromBody] _forgotPasswordRequest request)
        //{
        //    var _userCollection = _connectionService.db().GetCollection<Users>("Users");
        //    var user = _userCollection.Find(u => u.email == request.email).FirstOrDefault();

        //    if (user == null)
        //    {
        //        return Ok(new { message = "Bu e-posta adresiyle kayıtlı bir kullanıcı bulunamadı." });
        //    }

        //    // Yeni bir GUID oluştur
        //    user.passwordResetToken = Guid.NewGuid().ToString();
        //    user.TokenCreationDate = DateTimeOffset.UtcNow;

        //    _userCollection.ReplaceOne(u => u._id == user._id, user);
        //    #region e posta yolla aktif değil 
        //    //// E-posta gönderimi
        //    //var message = new MimeMessage();
        //    //message.From.Add(new MailboxAddress("Web Siteniz", "your_email@example.com"));
        //    //message.To.Add(new MailboxAddress(user.name, user.email));
        //    //message.Subject = "Şifre Sıfırlama Talimatları";

        //    //var resetLink = $"https://yoursite.com/reset-password?token={user.PasswordResetToken}"; // Gerçek bağlantınızı kullanın
        //    //message.Body = new TextPart("plain")
        //    //{
        //    //    Text = $"Şifrenizi sıfırlamak için aşağıdaki bağlantıya tıklayın:\n{resetLink}"
        //    //};

        //    //using (var client = new SmtpClient())
        //    //{
        //    //    client.Connect("smtp.example.com", 587, false); // SMTP sunucu bilgilerinizi kullanın
        //    //    client.Authenticate("your_email@example.com", "your_password"); // SMTP kullanıcı adı ve şifrenizi kullanın

        //    //    client.Send(message);
        //    //    client.Disconnect(true);
        //    //}
        //    #endregion
        //    return Ok(new { message = "Şifre sıfırlama talimatları e-posta adresinize gönderildi." });
        //}

        //#endregion

        #region setCulture
        public class _setCulture
        {
            [Required, MaxLength(2)]
            public string lan { get; set; }
        }
        [Route("[action]"), HttpPost]
        public IActionResult setCulture([FromBody] _setCulture culture)
        {
            try
            {
                if (!string.IsNullOrEmpty(culture.lan))
                {
                    if (config.supportedCultures.Contains(new CultureInfo(culture.lan)) && config.supportedCultures.Contains(new CultureInfo(culture.lan)))
                    {
                        HttpContext.Response.Cookies.Append(
                          CookieRequestCultureProvider.DefaultCookieName,
                          CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture.lan)),
                          new CookieOptions
                          {
                              Expires = DateTimeOffset.UtcNow.AddDays(1),
                              IsEssential = true,
                              HttpOnly = true
                          }
                          );
                    }
                }
            }
            catch { }
            return Ok();
        }
        #endregion

    }
}
