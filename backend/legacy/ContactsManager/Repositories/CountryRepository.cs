using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RepositryContracts;
using SerilogTimings;

namespace Repositories
{
    public class CountryRepository : CountryRepositryContract
    {
        private readonly AppDBContext _db;
        private readonly ILogger<CountryRepository> _logger;

        public CountryRepository(AppDBContext db, ILogger<CountryRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Country> AddCountry(Country country)
        {
            using (Operation.Time("AddCountry database operation for Country: {CountryName}", country?.CountryName))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Country: {@Country}",
                    nameof(AddCountry), DateTime.UtcNow, country);

                try
                {
                    if (country == null)
                    {
                        _logger.LogWarning("AddCountry called with null country parameter");
                        throw new ArgumentNullException(nameof(country));
                    }

                    _logger.LogDebug("Adding country with ID: {CountryId}, Name: {CountryName} to database",
                        country.CountryId, country.CountryName);

                    _db.Countries.Add(country);
                    await _db.SaveChangesAsync();

                    _logger.LogInformation("Successfully added country with ID: {CountryId}, Name: {CountryName}",
                        country.CountryId, country.CountryName);

                    return country;
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Database update error while adding country. Country: {@Country}. Error: {ErrorMessage}",
                        country, ex.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in {MethodName} for country: {@Country}",
                        nameof(AddCountry), country);
                    throw;
                }
            }
        }

        public async Task<IEnumerable<Country>> GetAllCountries()
        {
            using (Operation.Time("GetAllCountries database operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetAllCountries), DateTime.UtcNow);

                try
                {
                    var countries = await _db.Countries.ToListAsync();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} countries",
                        nameof(GetAllCountries), countries.Count);

                    return countries;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method", nameof(GetAllCountries));
                    throw;
                }
            }
        }

        public async Task<Country?> GetCountryByName(string name)
        {
            using (Operation.Time("GetCountryByName database query for name: {CountryName}", name))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Country name: {CountryName}",
                    nameof(GetCountryByName), DateTime.UtcNow, name);

                try
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        _logger.LogWarning("GetCountryByName called with null or empty name");
                        return null;
                    }

                    var country = await _db.Countries.FirstOrDefaultAsync(c => c.CountryName == name);

                    if (country == null)
                    {
                        _logger.LogInformation("No country found with name: {CountryName}", name);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully retrieved country with ID: {CountryId}, Name: {CountryName}",
                            country.CountryId, country.CountryName);
                    }

                    return country;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} for country name: {CountryName}",
                        nameof(GetCountryByName), name);
                    throw;
                }
            }
        }

        public async Task<Country?> GetCountryById(Guid? id)
        {
            using (Operation.Time("GetCountryById database query for ID: {CountryId}", id))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Country ID: {CountryId}",
                    nameof(GetCountryById), DateTime.UtcNow, id);

                try
                {
                    if (id == null || id == Guid.Empty)
                    {
                        _logger.LogWarning("GetCountryById called with invalid ID: {CountryId}", id);
                        return null;
                    }

                    var country = await _db.Countries.FindAsync(id);

                    if (country == null)
                    {
                        _logger.LogInformation("No country found with ID: {CountryId}", id);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully retrieved country with ID: {CountryId}, Name: {CountryName}",
                            country.CountryId, country.CountryName);
                    }

                    return country;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} for country ID: {CountryId}",
                        nameof(GetCountryById), id);
                    throw;
                }
            }
        }

        public Task<Country> UpdateCountry(Country country)
        {
            using (Operation.Time("UpdateCountry called (NOT IMPLEMENTED)"))
            {
                _logger.LogWarning("Executing {MethodName} method at {Timestamp} - NOT IMPLEMENTED. Country: {@Country}",
                    nameof(UpdateCountry), DateTime.UtcNow, country);

                throw new NotImplementedException("UpdateCountry method is not implemented in CountryRepository");
            }
        }
    }
}