var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddControllers();
builder.Services.AddControllersWithViews().AddNewtonsoftJson();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ? Add CORS service
builder.Services.AddCors();

var app = builder.Build();

// Routing first
app.UseRouting();

// ? Apply CORS (allow all for testing)
app.UseCors(x =>
    x.AllowAnyOrigin()
     .AllowAnyHeader()
     .AllowAnyMethod()
);

// Swagger in dev
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
