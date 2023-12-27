using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using PaymentWall.Models;
using PaymentWall.Services;
using MongoDB.Driver;
using PaymentWall.Attributes;
using System.Linq;

namespace PaymentWall.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SiteController : ControllerBase
    {
        private readonly IConnectionService _connectionService;

        public SiteController(IConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        #region GET SITE SETTINGS
        public class GetSiteSettingsResponse
        {
            public string Type { get; set; }
            public string Message { get; set; }
            public SiteViewModel SiteSettings { get; set; }
        }

        [HttpGet]
        [CheckAdminLogin(1)]
        public IActionResult GetSiteSettings()
        {
            var _siteCollection = _connectionService.db().GetCollection<Site>("Site");
            var siteSettings = _siteCollection.AsQueryable().FirstOrDefault();
            if (siteSettings == null)
            {
                return Ok(new GetSiteSettingsResponse { Type = "error", Message = "Site settings not found" });
            }

            var siteViewModel = new SiteViewModel
            {
                id = siteSettings._id.ToString(),
                name = siteSettings.name,
                domain = siteSettings.domain,
                detail = siteSettings.detail,
                tax = siteSettings.tax,
                currency = siteSettings.currency,
                fees = siteSettings.fees,
                maxFailedLoginAttempts = siteSettings.maxFailedLoginAttempts,
                email = siteSettings.email,
                phone = siteSettings.phone,
                currencyIcon = siteSettings.currencyIcon
            };

            return Ok(new GetSiteSettingsResponse
            {
                Type = "success",
                Message = "Site settings retrieved successfully",
                SiteSettings = siteViewModel
            });
        }
        #endregion

        #region Update Site Settings

        public class _updateSiteSettingsReq
        {
            public SiteViewModel siteSettings { get; set; }
        }

        public class _updateSiteSettingsRes
        {
            public string type { get; set; }
            public string message { get; set; }
        }

        public class SiteViewModel
        {
            public string id { get; set; }
            public string name { get; set; }
            public string domain { get; set; }
            public string detail { get; set; }
            public decimal tax { get; set; }
            public string currency { get; set; }
            public decimal fees { get; set; }
            public int maxFailedLoginAttempts { get; set; }
            public string email { get; set; }
            public string phone { get; set; }
            public string currencyIcon { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
        public IActionResult UpdateSiteSettings([FromBody] _updateSiteSettingsReq req)
        {
            if (req.siteSettings == null)
            {
                return Ok(new _updateSiteSettingsRes { type = "error", message = "Invalid request" });
            }

            var _siteCollection = _connectionService.db().GetCollection<Site>("Site");

            var siteSettingsToUpdate = _siteCollection.AsQueryable()
                .FirstOrDefault(s => s._id == new ObjectId(req.siteSettings.id));

            if (siteSettingsToUpdate == null)
            {
                return Ok(new _updateSiteSettingsRes { type = "error", message = "Site settings not found" });
            }

            siteSettingsToUpdate.name = req.siteSettings.name;
            siteSettingsToUpdate.domain = req.siteSettings.domain;
            siteSettingsToUpdate.detail = req.siteSettings.detail;
            siteSettingsToUpdate.tax = req.siteSettings.tax;
            siteSettingsToUpdate.currency = req.siteSettings.currency;
            siteSettingsToUpdate.fees = req.siteSettings.fees;
            siteSettingsToUpdate.maxFailedLoginAttempts = req.siteSettings.maxFailedLoginAttempts;
            siteSettingsToUpdate.email = req.siteSettings.email;
            siteSettingsToUpdate.phone = req.siteSettings.phone;
            siteSettingsToUpdate.currencyIcon = req.siteSettings.currencyIcon;

            var filter = Builders<Site>.Filter.Eq(s => s._id, new ObjectId(req.siteSettings.id));
            _siteCollection.ReplaceOne(filter, siteSettingsToUpdate);

            return Ok(new _updateSiteSettingsRes { type = "success", message = "Site settings updated successfully" });
        }

        #endregion

        #region Create Site Settings

        public class _createSiteSettingsReq
        {
            public SiteViewModel siteSettings { get; set; }
        }

        public class _createSiteSettingsRes
        {
            public string type { get; set; }
            public string message { get; set; }
        }

        [HttpPost("[action]")]
        [CheckAdminLogin(1)]
        public IActionResult CreateSiteSettings([FromBody] _createSiteSettingsReq req)
        {
            if (req.siteSettings == null)
            {
                return Ok(new _createSiteSettingsRes { type = "error", message = "Invalid request" });
            }

            var _siteCollection = _connectionService.db().GetCollection<Site>("Site");

            var newSiteSettings = new Site
            {
                name = req.siteSettings.name,
                domain = req.siteSettings.domain,
                detail = req.siteSettings.detail,
                tax = req.siteSettings.tax,
                currency = req.siteSettings.currency,
                fees = req.siteSettings.fees,
                maxFailedLoginAttempts = req.siteSettings.maxFailedLoginAttempts,
                email = req.siteSettings.email,
                phone = req.siteSettings.phone,
                currencyIcon = req.siteSettings.currencyIcon
            };

            _siteCollection.InsertOne(newSiteSettings);

            return Ok(new _createSiteSettingsRes { type = "success", message = "Site settings created successfully" });
        }

        #endregion


    }
}
