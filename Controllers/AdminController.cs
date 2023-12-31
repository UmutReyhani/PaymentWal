﻿using Amazon.Runtime.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
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
using static PaymentWall.Controllers.AdminController;
using static PaymentWall.Controllers.UserController;

namespace PaymentWall.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly IConnectionService _connectionService;
        private readonly IStringLocalizer _localizer;

        public AdminController(IConnectionService connectionService, IStringLocalizer<UserController> localizer)
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
            public int role { get; set; }
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
                return Ok(new _createAdminRes { type = "error", message = _localizer["12"].Value });
            }
            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");
            var adminIdFromSession = HttpContext.Session.GetString("id");

            var existingAdminByEmail = _adminCollection.AsQueryable().FirstOrDefault(a => a.email == data.email); //uniq index

            if (existingAdminByEmail != null)
            {
                return Ok(new _createAdminRes { type = "error", message = _localizer["13"].Value });
            }

            Admin newAdmin = new Admin
            {
                name = data.name,
                email = data.email,
                password = ComputeSha256Hash(data.password),
                role = data.role,
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
                reason = _localizer["14"].Value,
                role = data.role,
                adminId = newAdmin._id,
            };
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");
            _adminLogCollection.InsertOne(adminLog);

            return Ok(new _createAdminRes { type = "success", message = _localizer["15"].Value });
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
                return Ok(new _adminLoginRes { type = "error", message = _localizer["16"].Value });
            }

            var correctCaptchaAnswer = HttpContext.Session.GetString("CaptchaAnswer");
            if (loginData.captchaResponse != correctCaptchaAnswer)
            {
                return Ok(new _adminLoginRes { type = "error", message = _localizer["17"].Value });
            }

            if (loginData == null || string.IsNullOrEmpty(loginData.email) || string.IsNullOrEmpty(loginData.password))
            {
                return Ok(new _adminLoginRes { type = "error", message = _localizer["18"].Value });
            }

            var adminInDb = GetAdminFromDb(loginData.email);
            if (adminInDb == null)
            {
                return Ok(new _adminLoginRes { type = "error", message = _localizer["19"].Value });
            }

            var siteSettings = GetSiteSettings() ?? new Site { maxFailedLoginAttempts = 5 };

            if (adminInDb.active == 0)
            {
                return Ok(new _adminLoginRes { type = "error", message = _localizer["20"].Value });
            }


            if (adminInDb.password != ComputeSha256Hash(loginData.password))
            {
                HandleFailedAdminLogin(adminInDb, adminIpAddress);
                HttpContext.Session.Remove("CaptchaAnswer");
                return Ok(new _adminLoginRes { type = "error", message = _localizer["21"].Value });
            }

            HandleSuccessfulAdminLogin(adminInDb);
            HttpContext.Session.Remove("CaptchaAnswer");
            return Ok(new _adminLoginRes { type = "success", message = _localizer["22"].Value });
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
            wrongPassword(ip, true);

            admin.failedLoginAttempts += 1;

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
                adminId = admin._id,
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

        #region Check Admin Login

        public class _checkAdminLoginResData
        {
            public string id { get; set; }
            public string name { get; set; }
            public string email { get; set; }
            public DateTimeOffset lastLogin { get; set; }
            public int role { get; set; }
            public int active { get; set; }


        }

        public class _checkAdminLoginRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
            public _checkAdminLoginResData? data { get; set; }
        }

        [HttpPost("[action]")]
        public ActionResult<_checkAdminLoginRes> CheckAdminLogin()
        {
            var adminIdString = HttpContext.Session.GetString("id");

            if (string.IsNullOrEmpty(adminIdString))
            {
                return Ok(new _checkAdminLoginRes { type = "error", message = _localizer["adminNotLoggedIn"].Value });
            }

            if (!ObjectId.TryParse(adminIdString, out var adminId))
            {
                return Ok(new _checkAdminLoginRes { type = "error", message = _localizer["sessionError"].Value });
            }

            var adminCollection = _connectionService.db().GetCollection<Admin>("Admin");
            var admin = adminCollection.AsQueryable()
                                       .FirstOrDefault(a => a._id == adminId);
            if (admin == null)
            {
                return Ok(new _checkAdminLoginRes { type = "error", message = _localizer["adminNotFound"].Value });
            }

            if (admin.active != 1)
            {
                return Ok(new _checkAdminLoginRes { type = "error", message = _localizer["adminNotActiveOrBanned"].Value });
            }

            var adminDetails = new _checkAdminLoginResData
            {
                id = admin._id.ToString(),
                name = admin.name,
                email = admin.email,
                role = admin.role,
                active = admin.active,
                lastLogin = admin.lastLogin
            };

            return Ok(new _checkAdminLoginRes { type = "success", message = _localizer["adminIsLoggedIn"].Value, data = adminDetails });
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
        [CheckAdminLogin(0, 1)]
        public ActionResult<_logoutRes> AdminLogout()
        {
            var userId = HttpContext.Session.GetString("id");

            LogAdminAction(userId, 2);

            userFunctions.ClearCurrentAdminFromSession(HttpContext);
            return Ok(new _logoutRes { type = "success", message = _localizer["23"].Value });
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
                return Ok(new _deleteAdminRes { type = "error", message = _localizer["24"].Value });
            }

            await LogAdminDeletion(adminIdToDelete.ToString(), HttpContext.Session.GetString("id"));

            _adminCollection.DeleteOne(admin => admin._id == adminIdToDelete);

            return Ok(new _deleteAdminRes { type = "success", message = _localizer["25"].Value });

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

        [HttpPost("[action]"), CheckAdminLogin(0, 1)]
        public async Task<ActionResult<_updateAdminPasswordRes>> UpdateAdminPassword([FromBody] _updateAdminPasswordReq req)
        {
            var adminIdFromSession = HttpContext.Session.GetString("adminId");
            if (string.IsNullOrEmpty(adminIdFromSession))
            {
                return Ok(new _updateAdminPasswordRes { type = "error", message = _localizer["27"].Value });
            }

            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admins");

            var existingAdmin = _adminCollection.AsQueryable().FirstOrDefault(a => a._id.ToString() == adminIdFromSession);
            if (existingAdmin == null)
            {
                return Ok(new _updateAdminPasswordRes { type = "error", message = _localizer["28"].Value });
            }

            if (ComputeSha256Hash(req.oldPassword) != existingAdmin.password)
            {
                return Ok(new _updateAdminPasswordRes { type = "error", message = _localizer["29"].Value });
            }

            var passwordUpdate = Builders<Admin>.Update.Set(a => a.password, ComputeSha256Hash(req.newPassword));
            await _adminCollection.UpdateOneAsync(a => a._id == existingAdmin._id, passwordUpdate);

            return Ok(new _updateAdminPasswordRes { type = "success", message = _localizer["30"].Value });
        }

        #endregion

        #region Get All Admins
        public class adminFilterReq
        {
            public DateTime? startDate { get; set; }
            public DateTime? endDate { get; set; }
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
            public int? role { get; set; }
            public int? active { get; set; }
        }

        public class _getAllAdminsRes
        {
            public string type { get; set; }
            public string message { get; set; }
            public List<AdminDto> admins { get; set; }
            public int totalAdminsCount { get; set; }
        }

        public class AdminDto
        {
            public string _id { get; set; }
            public string name { get; set; }
            public string email { get; set; }
            public int role { get; set; }
            public int active { get; set; }
            public int failedLoginAttempts { get; set; }
            public DateTimeOffset lastLogin { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
        public ActionResult<_getAllAdminsRes> GetAllAdmins([FromBody] adminFilterReq req)
        {
            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");
            IQueryable<Admin> queryableAdmins = _adminCollection.AsQueryable();

            if (req.startDate.HasValue && req.endDate.HasValue)
            {
                queryableAdmins = queryableAdmins.Where(a => a.lastLogin >= req.startDate.Value && a.lastLogin <= req.endDate.Value);
            }

            if (req.role.HasValue)
            {
                queryableAdmins = queryableAdmins.Where(a => a.role == req.role.Value);
            }

            if (req.active.HasValue)
            {
                queryableAdmins = queryableAdmins.Where(a => a.active == req.active.Value);
            }

            int totalAdminsCount = queryableAdmins.Count();

            int currentPage = req.page.HasValue && req.page > 0 ? req.page.Value : 1;
            int pageSize = req.pageSize > 0 ? req.pageSize : 10;

            var admins = queryableAdmins
                        .Skip((currentPage - 1) * pageSize)
                        .Take(pageSize)
                        .Select(admin => new AdminDto
                        {
                            _id = admin._id.ToString(),
                            name = admin.name,
                            email = admin.email,
                            role = admin.role,
                            active = admin.active,
                            failedLoginAttempts = admin.failedLoginAttempts,
                            lastLogin = admin.lastLogin
                        })
                        .ToList();

            if (admins == null || admins.Count == 0)
            {
                return Ok(new _getAllAdminsRes { type = "error", message = _localizer["No admins found"].Value });
            }

            return Ok(new _getAllAdminsRes
            {
                type = "success",
                message = _localizer["Admins fetched successfully"].Value,
                admins = admins,
                totalAdminsCount = totalAdminsCount
            });
        }

        #endregion

        #region Update Admin Status

        public class _updateAdminStatusReq
        {
            [Required]
            public string adminId { get; set; }
            [Required]
            public int status { get; set; }
        }

        public class _updateAdminStatusRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
        public async Task<ActionResult<_updateAdminStatusRes>> UpdateAdminStatus([FromBody] _updateAdminStatusReq req)
        {
            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

            var existingAdmin = _adminCollection.AsQueryable().FirstOrDefault(a => a._id.ToString() == req.adminId);
            if (existingAdmin == null)
            {
                return Ok(new _updateAdminStatusRes { type = "error", message = _localizer["41"].Value });
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
                ip = HttpContext.Connection.RemoteIpAddress.ToString(),
                reason = "AdminStatusUpdate"
            };
            await _adminLogCollection.InsertOneAsync(adminLog);

            await _adminCollection.UpdateOneAsync(a => a._id == existingAdmin._id, statusUpdate);

            return Ok(new _updateAdminStatusRes { type = "success", message = _localizer["42"].Value });
        }


        #endregion

        #region Update User Status

        public class _updateUserStatusReq
        {
            [Required]
            public string userId { get; set; }
            [Required]
            public int status { get; set; }  // 0: passive, 1: active 2: banned
            public string reason { get; set; }  // Açıklama veya sebep

        }

        public class _updateUserStatusRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(0, 1)]
        public async Task<ActionResult<_updateUserStatusRes>> UpdateUserStatus([FromBody] _updateUserStatusReq req)
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

            var existingUser = _userCollection.AsQueryable().FirstOrDefault(u => u._id.ToString() == req.userId);
            if (existingUser == null)
            {
                return Ok(new _updateUserStatusRes { type = "error", message = _localizer["43"].Value });
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
                reason = req.reason,
                type = 3,
                userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                ip = HttpContext.Connection.RemoteIpAddress.ToString()
            };
            await _adminLogCollection.InsertOneAsync(adminLog);

            await _userCollection.UpdateOneAsync(u => u._id == existingUser._id, statusUpdate);

            return Ok(new _updateUserStatusRes { type = "success", message = _localizer["44"].Value });
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
            public int? status { get; set; } //0-1  passive-active

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

            DateTime startDate;
            DateTime endDate;
            if (!req.startDate.HasValue || !req.endDate.HasValue)
            {
                startDate = DateTime.Now.AddDays(-30);
                endDate = DateTime.Now;
            }
            else
            {
                startDate = req.startDate.Value;
                endDate = req.endDate.Value;
            }

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
            if (req.status.HasValue)
            {
                queryableUsers = queryableUsers.Where(u => u.status == req.status.Value);
            }
            int totalUsersCount = queryableUsers.Count();

            int currentPage = req.page.HasValue && req.page > 0 ? req.page.Value : 1;

            if (currentPage <= 0 || req.pageSize <= 0)
            {
                return Ok(new GetAllUsersRes { type = "false", message = _localizer["45"].Value });
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
            public string currency { get; set; }
        }

        public class _adminFinancialReportResponse
        {
            public string type { get; set; }
            public string message { get; set; }
            public decimal totalIncome { get; set; }
            public decimal totalExpense { get; set; }
            public decimal netBalance { get; set; }
        }

        [HttpPost("[action]"), CheckAdminLogin(0, 1)]
        public ActionResult<_adminFinancialReportResponse> GetSpecificUserFinancialReport([FromBody] _adminFinancialReportRequest request)
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");

            if (string.IsNullOrEmpty(request.email))
            {
                return Ok(new _adminFinancialReportResponse { type = "error", message = _localizer["46"].Value });
            }

            var user = _userCollection.AsQueryable().FirstOrDefault(u => u.email == request.email);

            if (user == null)
            {
                return Ok(new _adminFinancialReportResponse { type = "error", message = _localizer["47"].Value });
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

            int pageSize = request.pageSize.HasValue && request.pageSize.Value > 0 ? request.pageSize.Value : 10;
            int pageNumber = request.pageNumber.HasValue && request.pageNumber.Value > 0 ? request.pageNumber.Value : 1;

            var skip = (pageNumber - 1) * pageSize;

            DateTime startDate;
            DateTime endDate;
            if (!request.startDate.HasValue || !request.endDate.HasValue)
            {
                startDate = DateTime.Now.AddDays(-30);
                endDate = DateTime.Now;
            }
            else
            {
                startDate = request.startDate.Value;
                endDate = request.endDate.Value;
            }

            foreach (var wallet in userWallets)
            {
                var query = _accountingCollection.AsQueryable().Where(a => a.walletId == wallet._id);

                query = query.Where(a => a.date >= startDate && a.date <= endDate);

                if (!string.IsNullOrEmpty(request.currency))
                {
                    query = query.Where(a => a.currency == request.currency);
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
            public List<accountingDTO> transfers { get; set; }
            public int totalTransfersCount { get; set; }

        }

        public class accountingDTO
        {
            public string id { get; set; }
            public string userId { get; set; }
            public decimal amount { get; set; }
            public int walletId { get; set; }
            public decimal tax { get; set; }
            public decimal fees { get; set; }
            public string currency { get; set; }
            public string financialId { get; set; }
            public DateTimeOffset date { get; set; }
            public string recipientUserId { get; set; }
            public string senderUserId { get; set; }
            public string senderName { get; set; }
            public string recipientName { get; set; }
        }


        [HttpPost("[action]")]
        [CheckAdminLogin(0, 1)]
        public ActionResult<transferListResponse> GetAllTransfers([FromBody] transferFilterRequest filter)
        {
            var _accountingCollection = _connectionService.db().GetCollection<Accounting>("Accounting");
            var query = _accountingCollection.AsQueryable();

            DateTime startDate = filter.startDate ?? DateTime.Now.AddDays(-30);
            DateTime endDate = filter.endDate ?? DateTime.Now;

            query = query.Where(a => a.date >= startDate && a.date <= endDate);

            if (!string.IsNullOrEmpty(filter.userId))
            {
                ObjectId userIdObj = ObjectId.Parse(filter.userId);
                query = query.Where(a => a.userId == userIdObj);
            }

            int totalTransfersCount = query.Count();
            int pageNumber = filter.page ?? 1;
            int pageSize = filter.pageSize > 50 ? 50 : (filter.pageSize < 1 ? 10 : filter.pageSize);
            int skipAmount = (pageNumber - 1) * pageSize;
            var transfersQuery = query.Skip(skipAmount).Take(pageSize);

            var transfers = transfersQuery.ToList().Select(a => new accountingDTO
            {
                id = a._id.ToString(),
                userId = a.userId.ToString(),
                amount = a.amount,
                walletId = a.walletId,
                tax = a.tax,
                fees = a.fees,
                currency = a.currency,
                financialId = a.financialId?.ToString(),
                date = a.date,
                recipientUserId = a.recipientUserId.ToString(),
                senderUserId = a.senderUserId.ToString(),
                senderName = a.senderName,
                recipientName = a.recipientName
            }).ToList();

            var response = new transferListResponse
            {
                type = "success",
                message = "Transfers retrieved successfully.",
                transfers = transfers,
                totalTransfersCount = totalTransfersCount
            };

            return Ok(response);
        }
        #endregion

        #region UPDATE Wallet active/passive

        public class _updateWalletStatusReq
        {
            [Required]
            public string userId { get; set; }
            [Required]
            public int walletId { get; set; }
            [Required]
            public int status { get; set; }
        }

        public class _updateWalletStatusRes
        {
            [Required]
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(0, 1)]
        public async Task<ActionResult<_updateWalletStatusRes>> UpdateWalletStatus([FromBody] _updateWalletStatusReq req)
        {
            var userId = ObjectId.Parse(req.userId);
            var walletIdToUpdate = req.walletId;
            var newStatus = req.status;

            var _walletCollection = _connectionService.db().GetCollection<Wallet>("Wallet");
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

            var userWallets = _walletCollection.AsQueryable().Where(wallet => wallet.userId == userId).ToList();

            var walletToUpdate = userWallets.FirstOrDefault(wallet => wallet._id == walletIdToUpdate);

            if (walletToUpdate == null)
            {
                return Ok(new _updateWalletStatusRes { type = "error", message = "Wallet not found." });
            }

            if (walletToUpdate.status == newStatus)
            {
                return Ok(new _updateWalletStatusRes { type = "error", message = "Wallet status can't be same with updated status" });
            }

            var statusUpdate = Builders<Wallet>.Update
                .Set(w => w.status, newStatus);

            _walletCollection.UpdateOne(wallet => wallet._id == walletIdToUpdate, statusUpdate);

            var adminIdFromSession = HttpContext.Session.GetString("id");
            ObjectId adminObjectId = ObjectId.Parse(adminIdFromSession);

            var adminLog = new AdminLog
            {
                userId = walletToUpdate.userId,
                adminId = adminObjectId,
                previousStatus = walletToUpdate.status,
                updatedStatus = newStatus,
                date = DateTimeOffset.Now,
                type = 4,
                userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                ip = HttpContext.Connection.RemoteIpAddress.ToString()
            };
            await _adminLogCollection.InsertOneAsync(adminLog);

            var message = newStatus == 1 ? "active" : "passive";
            return Ok(new _updateWalletStatusRes { type = "success", message = $"Wallet status updated to {message}." });
        }
        #endregion

        #region Combined Limit Creation

        public class _setCombinedLimitReq
        {
            // Deposit Limits
            public decimal maxDeposit { get; set; }
            public decimal minDeposit { get; set; }
            public decimal dailyMaxDeposit { get; set; }
            public decimal monthlyMaxDeposit { get; set; }

            // Withdrawal Limits
            public decimal maxWithdrawal { get; set; }
            public decimal minWithdrawal { get; set; }
            public decimal dailyMaxWithdrawal { get; set; }
            public decimal monthlyMaxWithdrawal { get; set; }

            // Transfer Limits
            public decimal maxTransfer { get; set; }
            public decimal minTransfer { get; set; }
            public decimal dailyMaxTransfer { get; set; }
            public decimal monthlyMaxTransfer { get; set; }
            public int dailyMaxTransferCount { get; set; }
        }
        public class _setCombinedLimitRes
        {
            [Required]
            public string Type { get; set; }
            public string Message { get; set; }
        }
        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
        public ActionResult<_setCombinedLimitRes> AddCombinedLimit([FromBody] _setCombinedLimitReq req)
        {
            var _limitCollection = _connectionService.db().GetCollection<Limit>("Limit");
            var newLimit = new Limit
            {
                maxDeposit = req.maxDeposit,
                minDeposit = req.minDeposit,
                dailyMaxDeposit = req.dailyMaxDeposit,
                monthlyMaxDeposit = req.monthlyMaxDeposit,

                maxWithdrawal = req.maxWithdrawal,
                minWithdrawal = req.minWithdrawal,
                dailyMaxWithdrawal = req.dailyMaxWithdrawal,
                monthlyMaxWithdrawal = req.monthlyMaxWithdrawal,

                maxTransfer = req.maxTransfer,
                minTransfer = req.minTransfer,
                dailyMaxTransfer = req.dailyMaxTransfer,
                monthlyMaxTransfer = req.monthlyMaxTransfer,
                dailyMaxTransferCount = req.dailyMaxTransferCount
            };
            _limitCollection.InsertOne(newLimit);
            return Ok(new _setCombinedLimitRes { Type = "success", Message = _localizer["LimitsSett"].Value });
        }

        #endregion

        #region limit update
        public class _updateCombinedLimitReq
        {
            public string limitId { get; set; }

            // Deposit Limits
            public decimal? maxDeposit { get; set; }
            public decimal? minDeposit { get; set; }
            public decimal? dailyMaxDeposit { get; set; }
            public decimal? monthlyMaxDeposit { get; set; }

            // Withdrawal Limits
            public decimal? maxWithdrawal { get; set; }
            public decimal? minWithdrawal { get; set; }
            public decimal? dailyMaxWithdrawal { get; set; }
            public decimal? monthlyMaxWithdrawal { get; set; }

            // Transfer Limits
            public decimal? maxTransfer { get; set; }
            public decimal? minTransfer { get; set; }
            public decimal? dailyMaxTransfer { get; set; }
            public decimal? monthlyMaxTransfer { get; set; }
            public int? dailyMaxTransferCount { get; set; }
        }

        public class _updateCombinedLimitRes
        {
            [Required]
            public string Type { get; set; }
            public string Message { get; set; }
        }

        [HttpPost("UpdateCombinedLimit")]
        [CheckAdminLogin(1)]
        public async Task<ActionResult<_updateCombinedLimitRes>> UpdateCombinedLimit([FromBody] _updateCombinedLimitReq req)
        {
            var _limitCollection = _connectionService.db().GetCollection<Limit>("Limit");
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");
            var existingLimit = _limitCollection.AsQueryable().FirstOrDefault(l => l._id.ToString() == req.limitId);

            if (existingLimit == null)
            {
                return Ok(new _updateCombinedLimitRes { Type = "error", Message = _localizer["Limit not found"].Value });
            }

            var update = Builders<Limit>.Update;
            var updates = new List<UpdateDefinition<Limit>>();

            if (req.maxDeposit.HasValue) updates.Add(update.Set(l => l.maxDeposit, req.maxDeposit.Value));
            if (req.minDeposit.HasValue) updates.Add(update.Set(l => l.minDeposit, req.minDeposit.Value));
            if (req.dailyMaxDeposit.HasValue) updates.Add(update.Set(l => l.dailyMaxDeposit, req.dailyMaxDeposit.Value));
            if (req.monthlyMaxDeposit.HasValue) updates.Add(update.Set(l => l.monthlyMaxDeposit, req.monthlyMaxDeposit.Value));

            if (req.maxWithdrawal.HasValue) updates.Add(update.Set(l => l.maxWithdrawal, req.maxWithdrawal.Value));
            if (req.minWithdrawal.HasValue) updates.Add(update.Set(l => l.minWithdrawal, req.minWithdrawal.Value));
            if (req.dailyMaxWithdrawal.HasValue) updates.Add(update.Set(l => l.dailyMaxWithdrawal, req.dailyMaxWithdrawal.Value));
            if (req.monthlyMaxWithdrawal.HasValue) updates.Add(update.Set(l => l.monthlyMaxWithdrawal, req.monthlyMaxWithdrawal.Value));

            if (req.maxTransfer.HasValue) updates.Add(update.Set(l => l.maxTransfer, req.maxTransfer.Value));
            if (req.minTransfer.HasValue) updates.Add(update.Set(l => l.minTransfer, req.minTransfer.Value));
            if (req.dailyMaxTransfer.HasValue) updates.Add(update.Set(l => l.dailyMaxTransfer, req.dailyMaxTransfer.Value));
            if (req.monthlyMaxTransfer.HasValue) updates.Add(update.Set(l => l.monthlyMaxTransfer, req.monthlyMaxTransfer.Value));
            if (req.dailyMaxTransferCount.HasValue) updates.Add(update.Set(l => l.dailyMaxTransferCount, req.dailyMaxTransferCount.Value));

            var combinedUpdate = update.Combine(updates);
            await _limitCollection.UpdateOneAsync(l => l._id == existingLimit._id, combinedUpdate);


            var adminIdFromSession = HttpContext.Session.GetString("id");
            ObjectId adminObjectId = ObjectId.Parse(adminIdFromSession);
            var adminLog = new AdminLog
            {
                reason = "Updated Combined Limit",
                adminId = adminObjectId,
                date = DateTimeOffset.Now,
                type = 3,
                userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                ip = HttpContext.Connection.RemoteIpAddress.ToString()
            };
            await _adminLogCollection.InsertOneAsync(adminLog);

            return Ok(new _updateCombinedLimitRes { Type = "success", Message = _localizer["Limits updated successfully"].Value });
        }
        #endregion        

        #region Get Limits
        public class limitResponse
        {
            public string id { get; set; }
            public decimal maxDeposit { get; set; }
            public decimal minDeposit { get; set; }
            public decimal dailyMaxDeposit { get; set; }
            public decimal monthlyMaxDeposit { get; set; }
            public decimal maxWithdrawal { get; set; }
            public decimal minWithdrawal { get; set; }
            public decimal dailyMaxWithdrawal { get; set; }
            public decimal monthlyMaxWithdrawal { get; set; }
            public decimal maxTransfer { get; set; }
            public decimal minTransfer { get; set; }
            public decimal dailyMaxTransfer { get; set; }
            public decimal monthlyMaxTransfer { get; set; }
            public int dailyMaxTransferCount { get; set; }
        }

        [HttpGet("GetLimits")]
        public ActionResult<List<limitResponse>> GetLimits()
        {
            var _limitCollection = _connectionService.db().GetCollection<Limit>("Limit");
            var limits = _limitCollection.AsQueryable()
                .Select(l => new limitResponse
                {
                    id = l._id.ToString(),
                    maxDeposit = l.maxDeposit,
                    minDeposit = l.minDeposit,
                    dailyMaxDeposit = l.dailyMaxDeposit,
                    monthlyMaxDeposit = l.monthlyMaxDeposit,
                    maxWithdrawal = l.maxWithdrawal,
                    minWithdrawal = l.minWithdrawal,
                    dailyMaxWithdrawal = l.dailyMaxWithdrawal,
                    monthlyMaxWithdrawal = l.monthlyMaxWithdrawal,
                    maxTransfer = l.maxTransfer,
                    minTransfer = l.minTransfer,
                    dailyMaxTransfer = l.dailyMaxTransfer,
                    monthlyMaxTransfer = l.monthlyMaxTransfer,
                    dailyMaxTransferCount = l.dailyMaxTransferCount
                })
                .ToList();

            if (limits == null || limits.Count == 0)
            {
                return Ok(new { type = "error", message = "No limits found." });
            }

            return Ok(new { type = "success", message = "Limits retrieved successfully.", limits = limits });
        }

        #endregion

        #region GET USER COUNT
        public class GetUserCountsRes
        {
            [Required]
            public string type { get; set; }
            [Required]
            public string message { get; set; }
            public int totalUsersCount { get; set; }
            public int totalActiveUsersCount { get; set; }
        }

        [HttpGet("[action]")]
        [CheckAdminLogin(0, 1)]
        public ActionResult<GetUserCountsRes> GetUserCounts()
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            IQueryable<Users> queryableUsers = _userCollection.AsQueryable();

            int totalUsersCount = queryableUsers.Count();
            int totalActiveUsersCount = queryableUsers.Count(u => u.status == 1);

            return Ok(new GetUserCountsRes
            {
                type = "success",
                message = "User counts fetched successfully",
                totalUsersCount = totalUsersCount,
                totalActiveUsersCount = totalActiveUsersCount
            });
        }
        #endregion

        #region List Logs
        public class ListLogsReq
        {
            public DateTime? startDate { get; set; }
            public DateTime? endDate { get; set; }
            public int? pageNumber { get; set; } = 1;
            private int _pageSize = 10;
            public int pageSize
            {
                get => _pageSize;
                set => _pageSize = value > 50 ? 50 : value;
            }
        }

        public class ListLogsRes
        {
            public string type { get; set; }
            public List<LogViewModel> logs { get; set; }
            public int totalLogsCount { get; set; }

        }

        public class LogViewModel
        {
            public string id { get; set; }
            public string userId { get; set; }
            public DateTimeOffset date { get; set; }
            public string userAgent { get; set; }
            public string ip { get; set; }
            public int type { get; set; }
            public string name { get; set; } 
        }


        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
        public ActionResult<ListLogsRes> ListLogs([FromBody] ListLogsReq req)
        {
            var _logCollection = _connectionService.db().GetCollection<Log>("Logs");
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var query = _logCollection.AsQueryable();

            if (req.startDate.HasValue)
            {
                query = query.Where(l => l.date >= req.startDate.Value);
            }

            if (req.endDate.HasValue)
            {
                query = query.Where(l => l.date <= req.endDate.Value);
            }

            int totalLogsCount = query.Count();
            var skip = (req.pageNumber.Value - 1) * req.pageSize;
            var logsQuery = query.Skip(skip).Take(req.pageSize);

            var logs = logsQuery.ToList().Select(log => {
                var user = _userCollection.AsQueryable().FirstOrDefault(u => u._id == log.userId);
                return new LogViewModel
                {
                    id = log._id.ToString(),
                    userId = log.userId.ToString(),
                    date = log.date,
                    userAgent = log.userAgent,
                    ip = log.ip,
                    type = log.type,
                    name = user?.name
                };
            }).ToList();

            return Ok(new ListLogsRes { type = "success", logs = logs, totalLogsCount = totalLogsCount });
        }
        #endregion

        #region List Admin Logs
        public class ListAdminLogsReq
        {
            public DateTime? startDate { get; set; }
            public DateTime? endDate { get; set; }
            public int? pageNumber { get; set; } = 1;
            private int _pageSize = 10;
            public int pageSize
            {
                get => _pageSize;
                set => _pageSize = value > 50 ? 50 : value;
            }
        }

        public class ListAdminLogsRes
        {
            public string type { get; set; }
            public List<adminViewModelStatusUpdate> adminLogs { get; set; }
            public int totalLogsCount { get; set; }
        }
        public class adminViewModelStatusUpdate
        {
            public string userId { get; set; }
            [BsonRepresentation(BsonType.DateTime)]
            public DateTimeOffset date { get; set; }
            public string userAgent { get; set; }
            public string ip { get; set; }
            public int type { get; set; }  // register-login-logout-update-delete(0-1-2-3-4)
            public int? previousStatus { get; set; }
            public int updatedStatus { get; set; }
            public string reason { get; set; }
            public int role { get; set; }
            public string adminId { get; set; }
            public string? ticketId { get; set; }
            public int? previousTicketStatus { get; set; }
            public int? updatedTicketStatus { get; set; }
            public string? adminName { get; set; }
            public string? userSurname { get; set; }
            public string? userName { get; set; }

        }

        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
        public ActionResult<ListAdminLogsRes> ListAdminLogs([FromBody] ListAdminLogsReq req)
        {
            var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");
            var _adminCollection = _connectionService.db().GetCollection<Admin>("Admin");
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");

            var query = _adminLogCollection.AsQueryable();


            if (req.startDate.HasValue)
            {
                query = query.Where(a => a.date >= req.startDate.Value);
            }

            if (req.endDate.HasValue)
            {
                query = query.Where(a => a.date <= req.endDate.Value);
            }

            int totalLogsCount = query.Count();

            var skip = (req.pageNumber.Value - 1) * req.pageSize;
            var adminLogsQuery = query.Skip(skip).Take(req.pageSize);

            var adminLogs = adminLogsQuery.ToList().Select(log =>
            {
                var admin = _adminCollection.AsQueryable().FirstOrDefault(a => a._id == log.adminId);
                var user = _userCollection.AsQueryable().FirstOrDefault(u => u._id == log.userId);

                return new adminViewModelStatusUpdate
            {
                    userId = log.userId.ToString(),
                    userName = user?.name,
                    userSurname = user?.surname,
                    date = log.date,
                    userAgent = log.userAgent,
                    ip = log.ip,
                    type = log.type,
                    previousStatus = log.previousStatus,
                    updatedStatus = log.updatedStatus,
                    reason = log.reason,
                    role = log.role,
                    adminId = log.adminId.ToString(),
                    ticketId = log.ticketId.ToString(),
                    previousTicketStatus = log.previousTicketStatus,
                    updatedTicketStatus = log.updatedTicketStatus,
                    adminName = admin?.name,
            };
            }).ToList();

            return Ok(new ListAdminLogsRes { type = "success", adminLogs = adminLogs, totalLogsCount = totalLogsCount });
        }
        #endregion

        #region translate add
        public class AddTranslationRequest
        {
            public string _id { get; set; }
            public Dictionary<string, string> Translation { get; set; }
        }

        [HttpPost("AddTranslation")]
        [CheckAdminLogin(1)]
        public IActionResult AddTranslation([FromBody] AddTranslationRequest request)
        {
            var _translationProviderCollection = _connectionService.db().GetCollection<translationProvider>("translationProvider");

            if (request.Translation == null || !request.Translation.Any())
            {
                return Ok("Çeviri bilgisi boş olamaz.");
            }

            var newTranslation = new translationProvider
            {
                id = request._id.ToString(),
                translation = request.Translation
            };

            _translationProviderCollection.InsertOne(newTranslation);
            return Ok("Çeviri başarıyla eklendi.");


        }
        #endregion

        #region veri silme collection 

        [HttpPost("ClearCollection")]
        [CheckAdminLogin(1)]
        public IActionResult ClearCollection([FromBody] string collectionName)
        {
            if (string.IsNullOrEmpty(collectionName))
            {
                return BadRequest("Koleksiyon adı belirtilmeli.");
            }

            var _collection = _connectionService.db().GetCollection<BsonDocument>(collectionName);

            _collection.DeleteMany(Builders<BsonDocument>.Filter.Empty);

            return Ok($"{collectionName} koleksiyonu içindeki tüm veriler başarıyla temizlendi.");
        }


        #endregion

    }
}
