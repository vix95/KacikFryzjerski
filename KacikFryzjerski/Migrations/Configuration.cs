namespace KacikFryzjerski.Migrations
{
    using KacikFryzjerski.DAL;
    using KacikFryzjerski.Models;
    using System;
    using System.Data.Entity;
    using System.Data.Entity.Migrations;
    using System.Linq;

    public sealed class Configuration : DbMigrationsConfiguration<ProjectDbContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
            ContextKey = "ProjectDbContext";
        }

        protected override void Seed(ProjectDbContext context)
        {
            DbInitializer.SeedDbData(context);
            DbInitializer.SeedUsers(new ApplicationDbContext());
            //  This method will be called after migrating to the latest version.

            //  You can use the DbSet<T>.AddOrUpdate() helper extension method 
            //  to avoid creating duplicate seed data.
        }
    }
}
