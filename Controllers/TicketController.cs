using Microsoft.AspNetCore.Mvc;
using PaymentWall.Attributes;
using MongoDB.Driver;
using System.Collections.Generic;
using PaymentWall.Services;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using System.Net.Sockets;
using PaymentWall.Models;
using MongoDB.Driver.Linq;

[Route("api/[controller]")]
[ApiController]
public class TicketController : ControllerBase
{
    private readonly IConnectionService _connectionService;

    public TicketController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    #region Create Ticket

    public class _createTicketReq
    {
        [Required]
        public string title { get; set; }

        [Required]
        public string description { get; set; }
    }

    public class _createTicketRes
    {
        [Required]
        public string type { get; set; }
        public string message { get; set; }
    }

    [HttpPost("[action]"), CheckUserLogin]
    public ActionResult<Ticket> CreateTicket([FromBody] _createTicketReq req)
    {
        var userIdFromSession = HttpContext.Session.GetString("id");
        if (string.IsNullOrEmpty(userIdFromSession))
            return Unauthorized(new _createTicketRes { type = "error", message = "User not logged in." });

        Ticket ticket = new Ticket
        {
            userId = ObjectId.Parse(userIdFromSession),
            title = req.title,
            description = req.description,
            dateCreated = DateTimeOffset.UtcNow,
            status = 0
        };

        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");
        _ticketCollection.InsertOne(ticket);

        return Ok(new _createTicketRes { type = "success", message = "Ticket created successfully." });
    }

    #endregion

    #region List All Tickets by email
    public class _listAllTicketsReq
    {
        public string userEmail { get; set; }
        public DateTime? startDate { get; set; }
        public DateTime? endDate { get; set; }
        public int? pageNumber { get; set; }
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

    public class _listAllTicketsRes
    {
        public string type { get; set; }
        public List<Ticket> tickets { get; set; }
    }

    [HttpPost("[action]"), CheckAdminLogin(0, 1)]
    public ActionResult<_listAllTicketsRes> ListAllTicketsForAdmin([FromBody] _listAllTicketsReq request)
    {
        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");

        var query = _ticketCollection.AsQueryable();

        if (!string.IsNullOrEmpty(request.userEmail))
        {
            var _userCollection = _connectionService.db().GetCollection<Users>("Users");
            var user = _userCollection.AsQueryable().FirstOrDefault(u => u.email == request.userEmail);

            if (user != null)
            {
                query = query.Where(t => t.userId == user._id);
            }
        }

        if (request.startDate.HasValue)
        {
            query = query.Where(t => t.dateCreated >= request.startDate.Value);
        }

        if (request.endDate.HasValue)
        {
            query = query.Where(t => t.dateCreated <= request.endDate.Value);
        }

        if (!request.pageNumber.HasValue)
        {
            request.pageNumber = 1;
        }

        var skip = (request.pageNumber.Value - 1) * request.pageSize;

        var tickets = ((IMongoQueryable<Ticket>)query).Skip(skip).Take(request.pageSize).ToList();

        return Ok(new _listAllTicketsRes { type = "success", tickets = tickets });
    }


    #endregion

    #region Admin Response

    public class _adminResponseToTicketReq
    {
        [Required]
        public string ticketId { get; set; }

        [Required]
        public string response { get; set; }
    }

    public class _adminResponseToTicketRes
    {
        [Required]
        public string type { get; set; }
        public string message { get; set; }
    }

    [HttpPost("[action]"), CheckAdminLogin]
    public ActionResult<_adminResponseToTicketRes> AdminResponseToTicket([FromBody] _adminResponseToTicketReq data)
    {
        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");

        var ticket = _ticketCollection.Find(t => t._id == ObjectId.Parse(data.ticketId)).FirstOrDefault();
        if (ticket == null)
            return NotFound(new { type = "error", message = "Ticket not found." });

        ticket.adminResponse = data.response;
        ticket.status = 1;

        var update = Builders<Ticket>.Update
                                     .Set(t => t.adminResponse, data.response)
                                     .Set(t => t.status, 1);

        _ticketCollection.UpdateOne(t => t._id == ObjectId.Parse(data.ticketId), update);

        return Ok(new _adminResponseToTicketRes { type = "success", message = "Response added successfully." });
    }

    #endregion

    #region List User Tickets

    public class _listUserTicketsRes
    {
        [Required]
        public string type { get; set; }
        public List<Ticket> tickets { get; set; }
        public string message { get; set; }
    }

    public class ticketViewModel
    {
        public string title { get; set; }
        public string description { get; set; }
        public DateTimeOffset dateCreated { get; set; }
        public DateTimeOffset? dateResolved { get; set; }
        public string adminResponse { get; set; }
        public int status { get; set; }
    }



    [HttpGet("[action]"), CheckUserLogin]
    public ActionResult<_listUserTicketsRes> ListUserTickets()
    {
        var userIdFromSession = HttpContext.Session.GetString("id");
        if (string.IsNullOrEmpty(userIdFromSession))
            return Unauthorized(new { type = "error", message = "User not logged in." });

        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");

        var userTickets = _ticketCollection.Find(t => t.userId == ObjectId.Parse(userIdFromSession)).ToList();

        var ticketViewModels = userTickets.Select(t => new ticketViewModel
        {
            title = t.title,
            description = t.description,
            dateCreated = t.dateCreated,
            dateResolved = t.dateResolved,
            adminResponse = t.adminResponse,
            status = t.status,
        }).ToList();

        return Ok(new { type = "success", tickets = ticketViewModels });
    }


    #endregion

    #region Get User Ticket Detail

    public class _getUserTicketDetailRes
    {
        [Required]
        public string type { get; set; }
        public Ticket ticket { get; set; }
        public string message { get; set; }
    }

    [HttpGet("[action]/{ticketId}"), CheckUserLogin]
    public ActionResult<_getUserTicketDetailRes> GetUserTicketDetail(string ticketId)
    {
        var userIdFromSession = HttpContext.Session.GetString("id");
        if (string.IsNullOrEmpty(userIdFromSession))
            return Unauthorized(new { type = "error", message = "User not logged in." });

        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");
        var ticket = _ticketCollection.Find(t => t._id == ObjectId.Parse(ticketId) && t.userId == ObjectId.Parse(userIdFromSession)).FirstOrDefault();

        if (ticket == null)
            return NotFound(new _getUserTicketDetailRes { type = "error", message = "Ticket not found or you are not authorized to view it." });

        return Ok(new _getUserTicketDetailRes { type = "success", ticket = ticket });
    }

    #endregion

    #region Update Ticket Status

    public class _updateTicketStatusReq
    {
        [Required]
        public string ticketId { get; set; }
        [Required]
        public int status { get; set; }
        public string reason { get; set; }
    }

    public class _updateTicketStatusRes
    {
        [Required]
        public string type { get; set; }
        public string message { get; set; }
    }

    [HttpPost("[action]")]
    public async Task<ActionResult<_updateTicketStatusRes>> UpdateTicketStatus([FromBody] _updateTicketStatusReq req)
    {
        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");
        var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

        var existingTicket = _ticketCollection.AsQueryable().FirstOrDefault(t => t._id.ToString() == req.ticketId);
        if (existingTicket == null)
        {
            return Ok(new _updateTicketStatusRes { type = "error", message = "Ticket not found." });
        }

        var adminIdFromSession = HttpContext.Session.GetString("id");
        ObjectId adminObjectId = ObjectId.Parse(adminIdFromSession);

        var statusUpdate = Builders<Ticket>.Update
            .Set(t => t.status, req.status);

        var adminLog = new AdminLog
        {
            ticketId = existingTicket._id,
            adminId = adminObjectId,
            previousTicketStatus = existingTicket.status,
            updatedTicketStatus = req.status,
            date = DateTimeOffset.Now,
            type = 3,
            userAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
            ip = HttpContext.Connection.RemoteIpAddress.ToString(),
            reason = req.reason
        };
        await _adminLogCollection.InsertOneAsync(adminLog);

        await _ticketCollection.UpdateOneAsync(t => t._id == existingTicket._id, statusUpdate);

        return Ok(new _updateTicketStatusRes { type = "success", message = "Ticket status updated successfully." });
    }

    #endregion
}