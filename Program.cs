using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using PaymentWall;
using PaymentWall.Models;
using PaymentWall.Services;
using PaymentWall.Web.Tools.Converters.Swagger;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);



if (builder.Environment.IsDevelopment())
{
    config.isDevelopment = true;
}

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
#region dil 
builder.Services.AddDistributedMemoryCache().AddMemoryCache().AddLocalization();
builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
#endregion
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", builder => builder
    .WithOrigins("https://127.0.0.1:5500", "https://backend.tradeional.com")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
        );
});


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<ObjectIdOperationFilter>();
    options.SchemaFilter<ObjectIdSchemaFilter>();
});
builder.Services.Configure<DatabaseSettings>(
     builder.Configuration.GetSection("MyDb")
);
builder.Services.AddSingleton<IConnectionService, ConnectionService>();

// Add services to the container.
builder.Services.AddControllers();


var app = builder.Build();



app.UseHttpsRedirection();

app.UseCors("Default");
app.UseSession();


// dil baslangic
var options = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(new CultureInfo(config.defaultLanguage)),
    SupportedCultures = config.supportedCultures,
    SupportedUICultures = config.supportedCultures
};
app.UseRequestLocalization(options);
app.Use(async (context, next) =>
{
    var val = CookieRequestCultureProvider.ParseCookieValue(context.Request.Cookies[CookieRequestCultureProvider.DefaultCookieName]);
    if (val != null && val.Cultures.Count > 0)
    {
        var culture = new CultureInfo(val.Cultures.First().Value);
        if (config.supportedCultures.Contains(culture))
        {
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        else
        {
            context.Response.Cookies.Append(
                  CookieRequestCultureProvider.DefaultCookieName, // name of the cookie
                  CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(config.defaultLanguage)),  // create a string representation of the culture for storage
                  new CookieOptions
                  {
                      Expires = DateTimeOffset.UtcNow.AddDays(1),
                      IsEssential = true,
                      HttpOnly = true
                  }
                  );
        }
    }
    await next();
});


app.UseAuthorization();

// Configure the HTTP request pipeline.

app.UseSwagger();
app.UseSwaggerUI();

//dil bitis

//var db = config.createMapper();
//var cl = db.GetCollection<translationProvider>("translationProvider");
//cl.InsertOne(new translationProvider { id = "IlkDeger", translation = new Dictionary<string, string> { { "de", "Almanca" }, { "en", "Ýngilizce" } } });



app.MapControllers();

app.Run();