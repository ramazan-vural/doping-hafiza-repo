using doping_hafiza.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Canlı yayın operasyon paneli in-memory servisi (Oturum durumlarını request'ler arası korumak için Singleton)
builder.Services.AddSingleton<IBroadcastService, InMemoryBroadcastService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// Varsayılan route doğrudan Canlı Yayın Operasyon Paneline yönlendirilir
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Broadcast}/{action=Index}/{id?}");

app.Run();
