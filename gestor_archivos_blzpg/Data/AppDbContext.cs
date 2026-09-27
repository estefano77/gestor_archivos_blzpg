using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GestorArchivosBlzpg.Data;

namespace GestorArchivosBlzpg.Data;

/// <summary>
/// Contexto de datos. Mapea las tablas que ya existen en PostgreSQL.
/// </summary>
/// <remarks>
/// Este contexto no crea el esquema. Las tablas las crea
/// <c>supabase/migrations/0001_esquema_inicial.sql</c>, que es el unico sitio
/// donde vive el esquema, compartido con el proyecto <c>gestor_archivos_pg</c>.
/// Por eso no se usa <c>EnsureCreated</c> ni <c>Database.Migrate</c>: si esta
/// aplicacion crease el esquema, ambos proyectos podrian escribirlo de forma
/// distinta y acabar discrepando.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FileItem> Files => Set<FileItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            // snake_case en la base, PascalCase en C#. Sin esto habria que
            // mapear cada propiedad a mano.
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Password).HasColumnName("password");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.ProviderId).HasColumnName("provider_id");
            e.Property(x => x.AvatarColor).HasColumnName("avatar_color");
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        b.Entity<Folder>(e =>
        {
            e.ToTable("folders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Color).HasColumnName("color");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.User)
                .WithMany(x => x.Folders)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FileItem>(e =>
        {
            e.ToTable("files");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.FolderId).HasColumnName("folder_id");
            e.Property(x => x.OriginalName).HasColumnName("original_name");
            e.Property(x => x.MimeType).HasColumnName("mime_type");
            e.Property(x => x.Category).HasColumnName("category");
            e.Property(x => x.Size).HasColumnName("size");
            e.Property(x => x.StoragePath).HasColumnName("storage_path");
            e.Property(x => x.Description).HasColumnName("description");

            // text[] en PostgreSQL. Sin esto EF lo trataria como un string opaco
            // y la busqueda por etiqueta no encontraria nada.
            e.Property(x => x.Tags).HasColumnName("tags").HasColumnType("text[]");

            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.User)
                .WithMany(x => x.Files)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Sin cascade: borrar una carpeta borra antes sus archivos de forma
            // explicita, para poder quitar tambien sus binarios del almacen.
            // Si la base los borrara en cascada, los binarios quedarian huerfanos
            // sin que nadie los limpiera.
            e.HasOne(x => x.Folder)
                .WithMany(x => x.Files)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
