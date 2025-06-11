using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<TiendaContext>(options => options.UseSqlite("Data Source=tienda.db"));

// Agregar servicios CORS para permitir solicitudes desde el cliente
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowClientApp", policy =>
    {
        policy.WithOrigins("http://localhost:5177", "https://localhost:7221")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Agregar controladores si es necesario
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TiendaContext>();
    db.Database.EnsureCreated();

    if (!db.Productos.Any())
    {
        db.Productos.AddRange(new List<Producto> {
            new Producto { Nombre = "Producto 1", Descripcion = "Silla Gamer Vertagear SL5800 HygennX Carbon Black Ergonomic", Precio = 497.596M, Stock = 10, ImagenUrl = "https://example.com/producto1.jpg" },
            new Producto { Nombre = "Producto 2", Descripcion = "Mother Asrock B660M-HDV S1700 DDR4", Precio = 900.000M, Stock = 5, ImagenUrl = "https://example.com/producto2.jpg" },
            new Producto { Nombre = "Producto 3", Descripcion = "Monitor Lenovo ThinkVision S22i-30 21.5 FHD IPS 75Hz Anti Glare VESA", Precio = 149.999M, Stock = 5, ImagenUrl = "https://example.com/producto3.jpg" },
            new Producto { Nombre = "Producto 4", Descripcion = "Procesador Intel Core i9 14900K 6.0GHz Turbo Socket 1700 Raptor Lake", Precio = 673.665M, Stock = 0, ImagenUrl = "https://example.com/producto4.jpg" },
            new Producto { Nombre = "Producto 5", Descripcion = "Fuente Be Quiet 1000W 80 Plus Platinum STRAIGHT POWER 12 Full Modular ATX 3.1 PCIe 5.1", Precio = 306.450M, Stock = 20, ImagenUrl = "https://example.com/producto5.jpg" },
            new Producto { Nombre = "Producto 6", Descripcion = "Memoria Corsair DDR5 64GB (2x32GB) 6000MHz Dominator Titanium RGB CL30 Intel XMP3.0/AMD EXPO ICUE", Precio = 433.799M, Stock = 15, ImagenUrl = "https://example.com/producto6.jpg" },
            new Producto { Nombre = "Producto 7", Descripcion = "Teclado Hipermagnético Steel Series Apex PRO TKL 3 Wireless 2.4Ghz Bluetooth Black RGB Omnipoint 3.0 Hall Effect", Precio = 332.100M, Stock = 8, ImagenUrl = "https://example.com/producto7.jpg" },
            new Producto { Nombre = "Producto 8", Descripcion = "Mouse Logitech G Pro X Superlight Wireless", Precio = 120.000M, Stock = 12, ImagenUrl = "https://example.com/producto8.jpg" },
            new Producto { Nombre = "Producto 9", Descripcion = "Auriculares HyperX Cloud II", Precio = 80.000M, Stock = 7, ImagenUrl = "https://example.com/producto9.jpg" },
            new Producto { Nombre = "Producto 10", Descripcion = "Pad Mouse Redragon Flick XL", Precio = 15.000M, Stock = 25, ImagenUrl = "https://example.com/producto10.jpg" }
        });
        db.SaveChanges();
    }
}

// Configurar el pipeline de solicitudes HTTP
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Usar CORS con la política definida
app.UseCors("AllowClientApp");

// Mapear rutas básicas
app.MapGet("/", () => "Servidor API está en funcionamiento");

// Ejemplo de endpoint de API
app.MapGet("/api/datos", () => new { Mensaje = "Datos desde el servidor", Fecha = DateTime.Now });

// GET /productos (+ búsqueda por query)
app.MapGet("/productos", async (TiendaContext db, string? busqueda) =>
{
    if (string.IsNullOrWhiteSpace(busqueda))
        return await db.Productos.ToListAsync();

    return await db.Productos
        .Where(p => p.Nombre.Contains(busqueda) || p.Descripcion.Contains(busqueda))
        .ToListAsync();
});

// POST /carritos (inicializa el carrito)
app.MapPost("/carritos", async (TiendaContext db) =>
{
    var carrito = new Carrito { Items = new List<ItemCarrito>() };
    db.Carritos.Add(carrito);
    await db.SaveChangesAsync();
    return Results.Ok(carrito.Id);
});

// GET /carritos/{carritoId}
app.MapGet("/carritos/{carritoId}", async (TiendaContext db, int carritoId) =>
{
    var carrito = await db.Carritos
        .Include(c => c.Items)
        .ThenInclude(i => i.Producto)
        .FirstOrDefaultAsync(c => c.Id == carritoId);

    return carrito is null ? Results.NotFound() : Results.Ok(carrito);
});

// DELETE /carritos/{carritoId}
app.MapDelete("/carritos/{carritoId}", async (TiendaContext db, int carritoId) =>
{
    var carrito = await db.Carritos.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == carritoId);
    if (carrito is null) return Results.NotFound();
    db.ItemsCarrito.RemoveRange(carrito.Items);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// PUT /carritos/{carritoId}/confirmar (detalle + datos cliente)
app.MapPut("/carritos/{carritoId}/confirmar", async (TiendaContext db, int carritoId, CompraConfirmacionDto dto) =>
{
    var carrito = await db.Carritos.Include(c => c.Items).ThenInclude(i => i.Producto).FirstOrDefaultAsync(c => c.Id == carritoId);
    if (carrito is null) return Results.NotFound();

    // Validar stock
    foreach (var item in carrito.Items)
    {
        if (item.Cantidad > item.Producto.Stock)
            return Results.BadRequest($"No hay stock suficiente para {item.Producto.Nombre}");
    }

    // Descontar stock y registrar compra
    var compra = new Compra
    {
        Fecha = DateTime.Now,
        NombreCliente = dto.Nombre,
        ApellidoCliente = dto.Apellido,
        EmailCliente = dto.Email,
        Items = new List<ItemCompra>(),
        Total = 0
    };

    foreach (var item in carrito.Items)
    {
        item.Producto.Stock -= item.Cantidad;
        var itemCompra = new ItemCompra
        {
            ProductoId = item.ProductoId,
            Cantidad = item.Cantidad,
            PrecioUnitario = item.Producto.Precio
        };
        compra.Items.Add(itemCompra);
        compra.Total += itemCompra.Cantidad * itemCompra.PrecioUnitario;
    }

    db.Compras.Add(compra);
    db.ItemsCarrito.RemoveRange(carrito.Items);
    await db.SaveChangesAsync();
    return Results.Ok(compra);
});

// PUT /carritos/{carritoId}/{productoId} → Agrega o actualiza cantidad
app.MapPut("/carritos/{carritoId}/{productoId}", async (TiendaContext db, int carritoId, int productoId, int cantidad) =>
{
    var carrito = await db.Carritos.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == carritoId);
    var producto = await db.Productos.FindAsync(productoId);
    if (carrito is null || producto is null) return Results.NotFound();

    var item = carrito.Items.FirstOrDefault(i => i.ProductoId == productoId);
    if (item == null)
    {
        if (cantidad > producto.Stock) return Results.BadRequest("No hay stock suficiente");
        item = new ItemCarrito { ProductoId = productoId, Cantidad = cantidad };
        carrito.Items.Add(item);
    }
    else
    {
        if (cantidad > producto.Stock) return Results.BadRequest("No hay stock suficiente");
        item.Cantidad = cantidad;
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

// DELETE /carritos/{carritoId}/{productoId} → Elimina o reduce cantidad
app.MapDelete("/carritos/{carritoId}/{productoId}", async (TiendaContext db, int carritoId, int productoId) =>
{
    var carrito = await db.Carritos.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == carritoId);
    if (carrito is null) return Results.NotFound();
    var item = carrito.Items.FirstOrDefault(i => i.ProductoId == productoId);
    if (item is null) return Results.NotFound();
    db.ItemsCarrito.Remove(item);
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.Run();

