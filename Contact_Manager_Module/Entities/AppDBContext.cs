using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Entities
{
    public class AppDBContext : DbContext
    {
        public AppDBContext(DbContextOptions<AppDBContext> options)
        : base(options)
        {
        }

        public virtual DbSet<Person> Persons { get; set; }
        public virtual DbSet<Country> Countries { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Country>().ToTable("Countries");
            modelBuilder.Entity<Person>().ToTable("Persons");

            modelBuilder.Entity<Country>().HasData(
                new Country { CountryId = Guid.Parse("7C9E6645-3677-448A-95B7-511B41F17491"), CountryName = "Japan" },
                new Country { CountryId = Guid.Parse("A1B2C3D4-E5F6-47A8-B9C0-D1E2F3A4B5C6"), CountryName = "Canada" },
                new Country { CountryId = new Guid("4A91B323-6902-4D3E-B147-3A2F6990C254"), CountryName = "Norway" },
                new Country { CountryId = new Guid("99C6A23D-8D1E-4E90-95B6-03B576C75F71"), CountryName = "Australia" },
                new Country { CountryId = new Guid("F2345B12-1111-4A55-89CC-5521AABBCCDD"), CountryName = "Brazil" }
            );

            modelBuilder.Entity<Person>().HasData(
       new Person
       {
           PersonId = Guid.Parse("12345678-90AB-CDEF-1234-567890ABCDEF"),
           Name = "Alice",
           Gender = "Female",
           phone = "1111111111",
           DateOfBirth = new DateTime(1996, 5, 10),
           CountryId = Guid.Parse("7C9E6645-3677-448A-95B7-511B41F17491")
       },
       new Person
       {
           PersonId = Guid.Parse("23456789-0ABC-DEF1-2345-67890ABCDEFA"),
           Name = "Bob",
           Gender = "Male",
           phone = "2222222222",
           DateOfBirth = new DateTime(2001, 3, 15),
           CountryId = Guid.Parse("A1B2C3D4-E5F6-47A8-B9C0-D1E2F3A4B5C6")
       },
       new Person
       {
           PersonId = Guid.Parse("34567890-ABCD-EF12-3456-7890ABCDEFA1"),
           Name = "Charlie",
           Gender = "Male",
           phone = "3333333333",
           DateOfBirth = new DateTime(1991, 8, 20),
           CountryId = new Guid("4A91B323-6902-4D3E-B147-3A2F6990C254")
       },
       new Person
       {
           PersonId = Guid.Parse("4567890A-BCDE-F123-4567-890ABCDEFA12"),
           Name = "David",
           Gender = "Male",
           phone = "4444444444",
           DateOfBirth = new DateTime(1998, 11, 5),
           CountryId = new Guid("99C6A23D-8D1E-4E90-95B6-03B576C75F71")
       },
       new Person
       {
           PersonId = Guid.Parse("567890AB-CDEF-1234-5678-90ABCDEFA123"),
           Name = "Eve",
           Gender = "Female",
           phone = "5555555555",
           DateOfBirth = new DateTime(2004, 1, 25),
           CountryId = new Guid("F2345B12-1111-4A55-89CC-5521AABBCCDD")
       }
   );



        }
    }
};

