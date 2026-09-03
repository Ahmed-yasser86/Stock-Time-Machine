using Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceContracts.DTOs
{
    public class CountryResponse
    {
        public string CountryName { get; set; } 
        public Guid CountryId { get; set; }



        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;

            CountryResponse other = obj as CountryResponse;


            return  CountryName == other.CountryName &&
                CountryId == other.CountryId;   

        }


    }


    public static class ExtensionClass
    {
        public static  CountryResponse ConvertToDto(this Country country)
        {
            return new CountryResponse
            {
                CountryName = country.CountryName,
                CountryId = country.CountryId
            };
        }
    }
}
