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

        [HttpGet]
        [CheckAdminLogin(1)]
        public IActionResult GetSiteSettings()
        {
            var _siteCollection = _connectionService.db().GetCollection<Site>("Site");
            var siteSettings = _siteCollection.AsQueryable().FirstOrDefault();
            if (siteSettings == null)
            {
                return NotFound(new { type = "error", message = "Site settings not found" });
            }
            return Ok(siteSettings);
        }

        [HttpPut("{id}")]
        [CheckAdminLogin(1)]
        public IActionResult UpdateSiteSettings(string id, [FromBody] Site site)
        {
            if (site == null || site._id.ToString() != id)
            {
                return BadRequest();
            }

            var _siteCollection = _connectionService.db().GetCollection<Site>("Site");
            var siteSettings = _siteCollection.AsQueryable().FirstOrDefault(s => s._id == new ObjectId(id));

            if (siteSettings == null)
            {
                return NotFound(new { type = "error", message = "Site settings not found" });
            }

            _siteCollection.ReplaceOne(s => s._id == new ObjectId(id), site);

            return Ok(new { type = "success", message = "Site settings updated successfully" });
        }
    }
}
