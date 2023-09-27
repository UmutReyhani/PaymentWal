using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.ComponentModel.DataAnnotations;
using PaymentWall.Models;
using PaymentWall.Services;
using PaymentWall.User;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        public UserController(IConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

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
                var existingWallet = _walletCollection.Find<Wallet>(w => w._id == walletId).FirstOrDefault();
                if (existingWallet == null)
                {
                    isUnique = true;
                }
            } while (!isUnique);

            return walletId;
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
            public string type { get; set; } // personel-business(0-1)
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

        [HttpPost("createUser")]
        public ActionResult<_createUserRes> CreateUser([FromBody] _createUserReq data)
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _addressCollection = _connectionService.db().GetCollection<Address>("Addresses");
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");

            DateTime today = DateTime.Today;
            int age = today.Year - data.birthDate.Year;
            if (data.birthDate.Date > today.AddYears(-age)) age--;

            if (age < 18)
            {
                return Ok(new _createUserRes { type = "error", message = "must be at least 18 years old" });
            }

            var existingUserByEmail = _userCollection.Find<Users>(u => u.email == data.email).FirstOrDefault();

            if (existingUserByEmail != null)
            {
                return Ok(new _createUserRes { type = "error", message = "A user already exists with this mail." });
            }

            if (data.type != "0" && data.type != "1")
            {
                return Ok(new _createUserRes { type = "error", message = "type can only be '0' or '1'." });
            }

            Users newUser = new Users
            {
                name = data.name,
                surname = data.surname,
                birthDate = data.birthDate,
                email = data.email,
                password = ComputeSha256Hash(data.password),
                type = data.type,
                status = "1",
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
                type = "0"
            };
            var _logCollection = _connectionService.db().GetCollection<Log>("Log");
            _logCollection.InsertOne(userLog);


            return Ok(new _createUserRes { type = "success", message = "User created successfully." });

        }
        #endregion

        #region login user
        public class _loginReq
        {
            [Required]
            public string email { get; set; }
            [Required]
            public string password { get; set; }
        }

        public class _loginRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }
        [HttpPost("login")]
        public ActionResult<_loginRes> Login([FromBody] _loginReq loginData)
        {
            if (loginData == null || string.IsNullOrEmpty(loginData.email) || string.IsNullOrEmpty(loginData.password))
            {
                return Ok(new _loginRes { type = "error", message = "mail cant be null" });
            }

            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _siteCollection = _connectionService.db().GetCollection<Site>("Site");
            var _logCollection = _connectionService.db().GetCollection<Log>("Log");

            var siteSettings = _siteCollection.Find<Site>(Builders<Site>.Filter.Empty).FirstOrDefault();
            if (siteSettings == null)
            {
                siteSettings = new Site
                {
                    maxFailedLoginAttempts = 5,
                };
            }
            var userInDb = _userCollection.Find<Users>(u => u.email == loginData.email).FirstOrDefault();

            if (userInDb == null)
            {
                return Ok(new _loginRes { type = "error", message = "Kullanıcı bulunamadı." });
            }
            if (userInDb.status == "0")
            {
                return Ok(new _loginRes { type = "error", message = "Your account is inactive." });
            }

            if (userInDb.password != ComputeSha256Hash(loginData.password))
            {
                userInDb.failedLoginAttempts += 1;
                if (userInDb.failedLoginAttempts >= siteSettings.maxFailedLoginAttempts)
                {
                    userInDb.status = "0";
                }

                _userCollection.ReplaceOne(u => u._id == userInDb._id, userInDb);
                return Ok(new _loginRes { type = "error", message = "Wrong Password." });
            }

            userInDb.failedLoginAttempts = 0;

            var userIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers["User-Agent"].FirstOrDefault();

            Log userLog = new Log
            {
                userId = userInDb._id,
                date = DateTimeOffset.UtcNow,
                ip = userIpAddress,
                userAgent = userAgent,
                type = "1"
            };

            _logCollection.InsertOne(userLog);

            userInDb.lastLogin = DateTimeOffset.UtcNow;
            _userCollection.ReplaceOne(u => u._id == userInDb._id, userInDb);

            userFunctions.SetCurrentUserToSession(HttpContext, userInDb);

            return Ok(new _loginRes { type = "success", message = "Login Successfull" });
        }


        #endregion

        #region Update User

        public class _updateUserReq
        {
            [Required]
            public string userId { get; set; }
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

        [HttpPost("updateUser")]
        public async Task<ActionResult<_updateUserRes>> UpdateUser([FromBody] _updateUserReq req)
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _addressCollection = _connectionService.db().GetCollection<Address>("Addresses");

            var existingUser = await _userCollection.Find(u => u._id.ToString() == req.userId).FirstOrDefaultAsync();
            if (existingUser == null)
            {
                return Ok(new _updateUserRes { type = "error", message = "User not found." });
            }

            if (!string.IsNullOrEmpty(req.newPassword))
            {
                existingUser.password = ComputeSha256Hash(req.newPassword);
                var updatePassword = Builders<Users>.Update.Set(u => u.password, existingUser.password);
                await _userCollection.UpdateOneAsync(u => u._id.ToString() == existingUser._id.ToString(), updatePassword);
            }

            var existingAddress = await _addressCollection.Find(a => a.userId.ToString() == existingUser._id.ToString()).FirstOrDefaultAsync();
            if (existingAddress != null)
            {
                existingAddress.address = req.address ?? existingAddress.address;
                existingAddress.city = req.city ?? existingAddress.city;
                existingAddress.postCode = req.postCode ?? existingAddress.postCode;
                existingAddress.phoneNumber = req.phoneNumber ?? existingAddress.phoneNumber;
                await _addressCollection.ReplaceOneAsync(a => a._id.ToString() == existingAddress._id.ToString(), existingAddress);
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

            return Ok(new _updateUserRes { type = "success", message = "User updated successfully." });
        }
        #endregion

        #region Logout User
        public class _logoutRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("logout")]
        public ActionResult<_logoutRes> Logout()
        {
            userFunctions.ClearCurrentUserFromSession(HttpContext);

            return Ok(new _logoutRes { type = "success", message = "Logged out successfully." });
        }
        #endregion

    }

}
