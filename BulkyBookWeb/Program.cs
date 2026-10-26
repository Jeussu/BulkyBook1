using BulkyBook.DataAccess;
using BulkyBook.DataAccess.DbInitializer;
using BulkyBook.DataAccess.Repository;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Stripe;
using BulkyBookWeb.Services;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<ProductImageStore>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "text/css", "application/javascript", "image/svg+xml" });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("DefaultConnection")
    ));
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddIdentity<IdentityUser, IdentityRole>().AddDefaultTokenProviders()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IDbInitializer, DbInitializer>();
builder.Services.AddScoped<IDemoDataSeeder, DemoDataSeeder>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<IEmailSender>(serviceProvider => serviceProvider.GetRequiredService<EmailSender>());
builder.Services.AddSingleton<IEmailDeliveryService>(serviceProvider => serviceProvider.GetRequiredService<EmailSender>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorPages().AddRazorRuntimeCompilation();

var facebookAppId = builder.Configuration["Authentication:Facebook:AppId"];
var facebookAppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];
if (!string.IsNullOrWhiteSpace(facebookAppId) && !string.IsNullOrWhiteSpace(facebookAppSecret))
{
    builder.Services.AddAuthentication().AddFacebook(options =>
    {
        options.AppId = facebookAppId;
        options.AppSecret = facebookAppSecret;
    });
}

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = $"/Identity/Account/Login";
    options.LogoutPath = $"/Identity/Account/Logout";
    options.AccessDeniedPath = $"/Identity/Account/AccessDenied";
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(100);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
// Compress only public static text; account/customer HTML and JSON never enter this branch.
app.UseWhen(context =>
    context.Request.Path.StartsWithSegments("/css") || context.Request.Path.StartsWithSegments("/js") ||
    context.Request.Path.StartsWithSegments("/lib") || context.Request.Path.StartsWithSegments("/Identity/lib") ||
    (context.Request.Path.StartsWithSegments("/images") && context.Request.Path.Value!.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)),
    branch => branch.UseResponseCompression());
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) && path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentLength = 0;
        return;
    }
    var redirect = context.RequestServices.GetRequiredService<ProductImageStore>().CompatibilityRedirect(path);
    if (redirect is not null && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
    {
        context.Response.Headers.CacheControl = "public,max-age=300";
        context.Response.Redirect(redirect, permanent: true, preserveMethod: true);
        return;
    }
    await next();
});
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/images/products/optimized")
            ? "public,max-age=31536000,immutable"
            : context.Context.Request.Query.ContainsKey("v") ? "public,max-age=31536000,immutable" : "public,max-age=3600";
        context.Context.Response.Headers.XContentTypeOptions = "nosniff";
    }
});

app.UseRouting();

var stripeSecretKey = builder.Configuration["Stripe:SecretKey"];
if (!string.IsNullOrWhiteSpace(stripeSecretKey))
{
    StripeConfiguration.ApiKey = stripeSecretKey;
}
SeedDatabase();
SeedDemoData();
app.UseAuthentication();

app.UseAuthorization();
app.UseSession();
app.MapRazorPages();
app.MapGet("/health/release", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(new { revision = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion });
});
app.MapControllerRoute(
    name: "default",
    pattern: "{area=Customer}/{controller=Home}/{action=Index}/{id?}");

app.Run();

void SeedDatabase()
{
    using (var scope = app.Services.CreateScope())
    {
        var dbInitializer = scope.ServiceProvider.GetRequiredService<IDbInitializer>();
        dbInitializer.Initialize();
    }
}

void SeedDemoData()
{
    if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue<bool>("SeedData:EnableDemoData"))
    {
        return;
    }

    using (var scope = app.Services.CreateScope())
    {
        var demoDataSeeder = scope.ServiceProvider.GetRequiredService<IDemoDataSeeder>();
        demoDataSeeder.Seed();
    }
}
