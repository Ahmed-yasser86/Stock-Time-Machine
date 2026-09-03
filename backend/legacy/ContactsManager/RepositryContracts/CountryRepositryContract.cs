
using Entities;
using System;

namespace RepositryContracts
{
    public interface CountryRepositryContract
    { 

        Task<Country> AddCountry(Country country);


        Task<Country> UpdateCountry(Country country);


        Task<Country>? GetCountryById(Guid? id);

        Task<IEnumerable<Country>> GetAllCountries();

        Task<Country>? GetCountryByName(string name);

    }
}
