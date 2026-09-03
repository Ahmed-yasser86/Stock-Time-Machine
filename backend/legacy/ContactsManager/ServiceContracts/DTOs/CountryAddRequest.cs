using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities;
namespace ServiceContracts.DTOs
{
    public class CountryAddRequest
    {

        public string CountryName { get; set; }

        public Country ConvertToCountry()
        {

           return new Country
            {
                CountryName = this.CountryName
            };
        }


    }


   

}
