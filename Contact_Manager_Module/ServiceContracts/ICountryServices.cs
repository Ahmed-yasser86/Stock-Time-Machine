using Entities;
using ServiceContracts.DTOs;

namespace ServiceContracts
{
   
    public interface ICountryServices
     {

        public Task< CountryResponse >AddCountryRequest(CountryAddRequest? countryAddRequest);


        public Task<List<CountryResponse>> Countries();


        public Task<CountryResponse> GetCountryByCountryId(Guid? ID);
    }

}
