using Entities;
using Microsoft.EntityFrameworkCore;
using ServiceContracts;
using ServiceContracts.DTOs;
using Repositories;
using RepositryContracts;
using Microsoft.Extensions.Logging;
using SerilogTimings;

namespace Servicess
{
    public class CountryServices : ICountryServices
    {
        private readonly CountryRepositryContract CountriesRipositry;
        private readonly ILogger<CountryServices> _logger;

        public CountryServices(CountryRepositryContract countriesRipositry, ILogger<CountryServices> logger)
        {
            CountriesRipositry = countriesRipositry;
            _logger = logger;
        }

        public async Task<List<CountryResponse>> Countries()
        {
            using (Operation.Time("Get all countries operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(Countries), DateTime.UtcNow);

                try
                {
                    var countries = await CountriesRipositry.GetAllCountries();
                    var result = countries.Select(country => country.ConvertToDto()).ToList();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} countries",
                        nameof(Countries), result.Count);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method", nameof(Countries));
                    throw;
                }
            }
        }

        public async Task<CountryResponse> AddCountryRequest(CountryAddRequest? countryAddRequest)
        {
            using (Operation.Time("Add country operation for: {CountryName}", countryAddRequest?.CountryName ?? "null"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Request data: {@CountryAddRequest}",
                    nameof(AddCountryRequest), DateTime.UtcNow, countryAddRequest);

                try
                {
                    if (countryAddRequest == null)
                    {
                        _logger.LogWarning("AddCountryRequest called with null request parameter");
                        throw new ArgumentNullException(nameof(countryAddRequest));
                    }

                    if (string.IsNullOrEmpty(countryAddRequest.CountryName))
                    {
                        _logger.LogWarning("AddCountryRequest called with empty or null CountryName");
                        throw new ArgumentException("Country name cannot be null or empty.", nameof(countryAddRequest.CountryName));
                    }

                    _logger.LogDebug("Checking if country '{CountryName}' already exists", countryAddRequest.CountryName);

                    var existingCountry = await CountriesRipositry.GetCountryByName(countryAddRequest.CountryName);
                    if (existingCountry != null)
                    {
                        _logger.LogWarning("Attempted to add duplicate country '{CountryName}'", countryAddRequest.CountryName);
                        throw new ArgumentException($"Country with name {countryAddRequest.CountryName} already exists.", nameof(countryAddRequest.CountryName));
                    }

                    _logger.LogDebug("Converting CountryAddRequest to Country entity");
                    Country country = countryAddRequest.ConvertToCountry();
                    country.CountryId = Guid.NewGuid();

                    _logger.LogDebug("Adding new country with ID: {CountryId}, Name: {CountryName}",
                        country.CountryId, country.CountryName);

                    await CountriesRipositry.AddCountry(country);

                    var result = country.ConvertToDto();

                    _logger.LogInformation("Successfully added new country. ID: {CountryId}, Name: {CountryName}",
                        result.CountryId, result.CountryName);

                    return result;
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "Validation error in AddCountryRequest for country name: {CountryName}",
                        countryAddRequest?.CountryName ?? "null");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in AddCountryRequest for country: {CountryName}",
                        countryAddRequest?.CountryName ?? "null");
                    throw;
                }
            }
        }

        public async Task<CountryResponse?> GetCountryByCountryId(Guid? ID)
        {
            using (Operation.Time("Get country by ID operation for: {CountryId}", ID))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Looking for country with ID: {CountryId}",
                    nameof(GetCountryByCountryId), DateTime.UtcNow, ID);

                try
                {
                    if (ID == null)
                    {
                        _logger.LogWarning("GetCountryByCountryId called with null ID parameter");
                        return null;
                    }

                    _logger.LogDebug("Retrieving country with ID: {CountryId}", ID);

                    Country country = await CountriesRipositry.GetCountryById(ID);

                    if (country == null)
                    {
                        _logger.LogWarning("No country found with ID: {CountryId}", ID);
                        return null;
                    }

                    var result = country.ConvertToDto();

                    _logger.LogInformation("Successfully retrieved country with ID: {CountryId}, Name: {CountryName}",
                        result.CountryId, result.CountryName);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in GetCountryByCountryId for ID: {CountryId}", ID);
                    throw;
                }
            }
        }
    }
}