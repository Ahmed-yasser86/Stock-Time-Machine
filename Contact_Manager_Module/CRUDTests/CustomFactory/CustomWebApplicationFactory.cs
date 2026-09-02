using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ServiceContracts;
using ServiceContracts.DTOs;
using ServiceContracts.DTOs.Enums;
using System;
using System.Collections.Generic;
using System.Linq;


namespace CRUDTests.CustomFactory
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        public Mock<IPersonServices> PersonServicesMock { get; private set; }
        public Mock<ICountryServices> CountryServicesMock { get; private set; }

        public CustomWebApplicationFactory()
        {
            PersonServicesMock = new Mock<IPersonServices>();
            CountryServicesMock = new Mock<ICountryServices>();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var personServiceDescriptors = services.Where(d => d.ServiceType == typeof(IPersonServices)).ToList();
                foreach (var descriptor in personServiceDescriptors)
                {
                    services.Remove(descriptor);
                }

                var countryServiceDescriptors = services.Where(d => d.ServiceType == typeof(ICountryServices)).ToList();
                foreach (var descriptor in countryServiceDescriptors)
                {
                    services.Remove(descriptor);
                }

                // Add mocked services
                services.AddScoped(_ => PersonServicesMock.Object);
                services.AddScoped(_ => CountryServicesMock.Object);
            });
        }
    }
}