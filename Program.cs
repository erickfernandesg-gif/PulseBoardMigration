using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// 1. Lendo as chaves do appsettings.json
var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseKey = builder.Configuration["Supabase:Key"];

// 2. Criando o cliente do Supabase
var supabaseOptions = new Supabase.SupabaseOptions { AutoConnectRealtime = true };
var supabaseClient = new Supabase.Client(supabaseUrl, supabaseKey, supabaseOptions);

// 3. Injetando no sistema (Singleton significa que cria uma vez e usa no projeto todo)
builder.Services.AddSingleton(supabaseClient);

// 4. Registando o nosso serviço
builder.Services.AddScoped<PulseBoardMigration.Services.BoardService>();
builder.Services.AddScoped<PulseBoardMigration.Services.AuthService>();

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configura o sistema de autenticação por Cookie
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login"; // Se não estiver logado, manda pra cá
        options.LogoutPath = "/Auth/Logout";
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
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication(); // 1º Verifica quem é o usuário
app.UseAuthorization();  // 2º Verifica o que ele pode acessar

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
