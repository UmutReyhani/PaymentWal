using Microsoft.AspNetCore.Mvc;
using PaymentWall.Attributes;
using MongoDB.Driver;
using PaymentWall.Services;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using PaymentWall.Models;
using MongoDB.Driver.Linq;
using Microsoft.Extensions.Localization;
using PaymentWall.Controllers;
using System.Net.Mail;
using System.Net;

[Route("api/[controller]")]
[ApiController]
public class TicketController : ControllerBase
{
    private readonly IConnectionService _connectionService;
    private readonly IStringLocalizer _localizer;

    public TicketController(IConnectionService connectionService, IStringLocalizer<UserController> localizer)
    {
        _connectionService = connectionService;
        _localizer = localizer;
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
            return Unauthorized(new _createTicketRes { type = "error", message = _localizer["userNotLoggedIn"].Value });

        Ticket ticket = new Ticket
        {
            userId = ObjectId.Parse(userIdFromSession),
            title = req.title,
            description = req.description,
            date = DateTimeOffset.UtcNow,
            status = 0
        };

        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");
        _ticketCollection.InsertOne(ticket);

        return Ok(new _createTicketRes { type = "success", message = _localizer["ticketCreatedSuccessfully"].Value });
    }

    #endregion

    #region List All Tickets by email
    public class _listAllTicketsReq
    {
        public string? userEmail { get; set; }
        public int? status { get; set; }
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
        public List<TicketViewModels> tickets { get; set; }
        public int totalTickets { get; set; }

    }
    public class TicketViewModels
    {
        public string ticketId { get; set; }
        public string userId { get; set; }
        public string userEmail { get; set; }
        public string name { get; set; }
        public string surname { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public DateTimeOffset dateCreated { get; set; }
        public int status { get; set; }
    }

    [HttpPost("[action]"), CheckAdminLogin(0, 1)]
    public ActionResult<_listAllTicketsRes> ListAllTicketsForAdmin([FromBody] _listAllTicketsReq request)
    {
        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");
        var _userCollection = _connectionService.db().GetCollection<Users>("Users");

        var query = _ticketCollection.AsQueryable();

        if (!string.IsNullOrEmpty(request.userEmail))
        {
            var user = _userCollection.AsQueryable().FirstOrDefault(u => u.email == request.userEmail);
            if (user != null)
            {
                query = query.Where(t => t.userId == user._id);
            }
        }

        if (request.status.HasValue)
        {
            query = query.Where(t => t.status == request.status.Value);
        }

        if (request.startDate.HasValue)
        {
            query = query.Where(t => t.date >= request.startDate.Value);
        }

        if (request.endDate.HasValue)
        {
            query = query.Where(t => t.date <= request.endDate.Value);
        }

        int totalTickets = query.Count();

        if (!request.pageNumber.HasValue)
        {
            request.pageNumber = 1;
        }

        if (!request.pageNumber.HasValue || request.pageNumber < 1)
        {
            request.pageNumber = 1;
        }
        var skip = (request.pageNumber.Value - 1) * request.pageSize;
        var tickets = query.Skip(skip).Take(request.pageSize).ToList();

        var users = _userCollection.AsQueryable().ToList();
        var ticketViewModels = tickets.Select(t => {
            var user = users.FirstOrDefault(u => u._id == t.userId);
            return new TicketViewModels
            {
                ticketId = t._id.ToString(),
                userId = t.userId.ToString(),
                userEmail = user?.email,
                name = user?.name,
                surname = user?.surname,
                title = t.title,
                description = t.description,
                dateCreated = t.date,
                status = t.status
            };
        }).ToList();

        return Ok(new _listAllTicketsRes { type = "success", tickets = ticketViewModels, totalTickets = totalTickets });
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

    [HttpPost("[action]"), CheckAdminLogin(0, 1)]
    public ActionResult<_adminResponseToTicketRes> AdminResponseToTicket([FromBody] _adminResponseToTicketReq data)
    {
        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");

        var ticket = _ticketCollection.AsQueryable()
                                      .FirstOrDefault(t => t._id == ObjectId.Parse(data.ticketId));

        if (ticket == null)
            return Ok(new _adminResponseToTicketRes { type = "error", message = _localizer["ticketNotFound"].Value });

        var update = Builders<Ticket>.Update
                                     .Set(t => t.adminResponse, data.response)
                                     .Set(t => t.status, 1);

        _ticketCollection.UpdateOne(t => t._id == ObjectId.Parse(data.ticketId), update);

        return Ok(new _adminResponseToTicketRes { type = "success", message = _localizer["responseAddedSuccessfully"].Value });
    }

    #endregion

    #region List User Tickets

    public class _listUserTicketsRes
    {
        [Required]
        public string type { get; set; }
        public List<ticketViewModel> tickets { get; set; }
        public string message { get; set; }
        public int totalTicketsCount { get; set; }
    }
    public class ListUserTicketsRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    public class ticketViewModel
    {
        public string ticketId { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public DateTimeOffset dateCreated { get; set; }
        public DateTimeOffset? dateResolved { get; set; }
        public string adminResponse { get; set; }
        public int status { get; set; }
    }



    [HttpPost("[action]")]
    [CheckUserLogin]
    public ActionResult<_listUserTicketsRes> ListUserTickets([FromBody] ListUserTicketsRequest req)
    {
        var userIdFromSession = HttpContext.Session.GetString("id");
        if (string.IsNullOrEmpty(userIdFromSession))
        {
            return Unauthorized(new { type = "error", message = _localizer["userNotLoggedIn"].Value });
        }

        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");
        var query = _ticketCollection.AsQueryable()
                                     .Where(t => t.userId == ObjectId.Parse(userIdFromSession));

        int totalTicketsCount = query.Count();

        int pageNumber = req.Page < 1 ? 1 : req.Page;
        int pageSize = req.PageSize > 50 ? 50 : (req.PageSize < 1 ? 10 : req.PageSize);

        var userTickets = query.Skip((pageNumber - 1) * pageSize)
                               .Take(pageSize)
                               .ToList();

        var ticketViewModels = userTickets.Select(t => new ticketViewModel
        {
            ticketId = t._id.ToString(),
            title = t.title,
            description = t.description,
            dateCreated = t.date,
            dateResolved = t.dateResolved,
            adminResponse = t.adminResponse,
            status = t.status,
        }).ToList();

        return Ok(new _listUserTicketsRes
        {
            type = "success",
            tickets = ticketViewModels,
            totalTicketsCount = totalTicketsCount
        });
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
            return Unauthorized(new { type = "error", message = _localizer["userNotLoggedIn"].Value });

        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");
        var ticket = _ticketCollection.AsQueryable()
                                      .FirstOrDefault(t => t._id == ObjectId.Parse(ticketId) && t.userId == ObjectId.Parse(userIdFromSession));
        if (ticket == null)
            return Ok(new _getUserTicketDetailRes { type = "error", message = _localizer["ticketNotFoundOrNotAuthorized"].Value });

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

    [HttpPost("[action]"), CheckAdminLogin(0, 1)]
    public async Task<ActionResult<_updateTicketStatusRes>> UpdateTicketStatus([FromBody] _updateTicketStatusReq req)
    {
        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");
        var _adminLogCollection = _connectionService.db().GetCollection<AdminLog>("AdminLog");

        var existingTicket = _ticketCollection.AsQueryable().FirstOrDefault(t => t._id.ToString() == req.ticketId);
        if (existingTicket == null)
        {
            return Ok(new _updateTicketStatusRes { type = "error", message = _localizer["ticketNotFound"].Value });
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

        return Ok(new _updateTicketStatusRes { type = "success", message = _localizer["ticketStatusUpdated"].Value });
    }

    #endregion

    #region List Resolved Tickets

    public class ListResolvedTicketsRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    public class ResolvedTicketsResponse
    {
        public string type { get; set; }
        public string message { get; set; }
        public List<TicketViewModel> tickets { get; set; }
        public int totalTicketsCount { get; set; }
    }



    [HttpPost("[action]")]
    [CheckAdminLogin(0, 1)]
    public ActionResult<ResolvedTicketsResponse> ListResolvedTickets([FromBody] ListResolvedTicketsRequest request)
    {
        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");

        var query = _ticketCollection.AsQueryable()
            .Where(t => t.status == 3);

        int totalTicketsCount = query.Count();

        int pageNumber = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize > 50 ? 50 : (request.PageSize < 1 ? 10 : request.PageSize);

        var resolvedTickets = query.Skip((pageNumber - 1) * pageSize)
                                   .Take(pageSize)
                                   .ToList();
        var ticketViewModels = query.Skip((pageNumber - 1) * pageSize)
                            .Take(pageSize)
                            .Select(t => new TicketViewModel
                            {
                                userId = t.userId.ToString(),
                                ticketId = t._id.ToString(),
                                title = t.title,
                                description = t.description,
                                dateCreated = t.date
                            })
                            .ToList();

        var response = new ResolvedTicketsResponse
        {
            type = "success",
            message = "Resolved tickets retrieved successfully.",
            tickets = ticketViewModels,
            totalTicketsCount = totalTicketsCount
        };

        return Ok(response);
    }
    #endregion

    #region List Pending Tickets

    public class PendingTicketsResponse
    {
        [Required]
        public string type { get; set; }
        public string message { get; set; }
        public List<TicketViewModel> tickets { get; set; }
        public int totalCount { get; set; }
    }
    public class ListPendingTicketsRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
    public class TicketViewModel
    {
        public string userId { get; set; }
        public string ticketId { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public DateTimeOffset dateCreated { get; set; }
    }

    [HttpPost("[action]")]
    [CheckAdminLogin(0, 1)]
    public ActionResult<PendingTicketsResponse> ListPendingTicketsWithCount([FromBody] ListPendingTicketsRequest request)
    {
        var _ticketCollection = _connectionService.db().GetCollection<Ticket>("Tickets");

        var pendingTicketsQuery = _ticketCollection.AsQueryable()
            .Where(t => t.status == 0);

        // Toplam bilet sayısını hesaplama
        int totalTicketsCount = pendingTicketsQuery.Count();

        // Sayfa numarası ve sayfa boyutu kontrolü
        int pageNumber = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize > 50 ? 50 : (request.PageSize < 1 ? 10 : request.PageSize);

        // Sayfalama uygulama
        var pendingTickets = pendingTicketsQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var ticketViewModels = pendingTickets.Select(t => new TicketViewModel
        {
            userId = t.userId.ToString(),
            ticketId = t._id.ToString(),
            title = t.title,
            description = t.description,
            dateCreated = t.date
        }).ToList();

        var response = new PendingTicketsResponse
        {
            type = "success",
            message = "Pending tickets retrieved successfully.",
            tickets = ticketViewModels,
            totalCount = totalTicketsCount
        };

        return Ok(response);
    }
    #endregion

    //#region Contact Form

    //public class _contactFormRequest
    //{
    //    [Required]
    //    public string firstName { get; set; }

    //    [Required]
    //    public string lastName { get; set; }

    //    [Required, EmailAddress]
    //    public string email { get; set; }

    //    [Required]
    //    public string message { get; set; }
    //}

    //public class _contactFormResponse
    //{
    //    [Required]
    //    public string type { get; set; }
    //    public string message { get; set; }
    //}

    //[HttpPost("[action]")]
    //public ActionResult<_contactFormResponse> SendContactForm([FromBody] _contactFormRequest req)
    //{
    //    try
    //    {
    //        // E-posta gönderimini burada yapın
    //        MailMessage mail = new MailMessage();
    //        mail.From = new MailAddress("yourEmail@example.com");
    //        mail.To.Add("yourDestinationEmail@example.com");
    //        mail.Subject = $"New Contact Form Message from {req.firstName} {req.lastName}";
    //        mail.Body = $"{req.message}\n\nFrom: {req.email}";

    //        SmtpClient smtpClient = new SmtpClient("your.smtp.server");
    //        smtpClient.Port = 587;
    //        smtpClient.Credentials = new NetworkCredential("yourEmail@example.com", "yourPassword");
    //        smtpClient.EnableSsl = true;

    //        smtpClient.Send(mail);

    //        return Ok(new _contactFormResponse { type = "success", message = "Message sent successfully!" });
    //    }
    //    catch (Exception ex)
    //    {
    //        return BadRequest(new _contactFormResponse { type = "error", message = ex.Message });
    //    }
    //}

    //#endregion

}