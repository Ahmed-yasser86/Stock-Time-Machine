using Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceContracts.DTOs
{
    public class PersonRespones
    {


        public Guid PersonId { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? email { get; set; }

        public string phone { get; set; }

        public string Gender { get; set; }

        public string? Address { get; set; }

        public Guid CountryId { get; set; }

        public string CountryName { get; set; }

        public bool ?NewsLetter { get; set; }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;

            PersonRespones other = (PersonRespones)obj;

            return PersonId == other.PersonId &&
                   Name == other.Name &&
                   Age == other.Age &&
                   DateOfBirth == other.DateOfBirth &&
                   email == other.email &&
                   phone == other.phone &&
                   Gender == other.Gender &&
                   Address == other.Address &&
                   CountryId == other.CountryId &&
                   NewsLetter == other.NewsLetter &&  
                   CountryName == other.CountryName;

        }




        public PersonUpdateRequest ToPersonUpdateRequest()
        {
            return new PersonUpdateRequest
            {
                PersonId = this.PersonId,
                Name = this.Name,
                DateOfBirth = this.DateOfBirth,
                email = this.email,
                phone = this.phone,
                Gender = (Enums.GenderOptions)Enum.Parse(typeof(Enums.GenderOptions), Gender, true),
                Address = this.Address,
                CountryId = (Guid)this.CountryId,
                NewsLetter = this.NewsLetter
            };



        }
        public override int GetHashCode()
        {
            return base.GetHashCode();
        } 
    }

        public static class PersonResponseExtension
        {
            public static PersonRespones ConvertToPersonRespons(this Person person)
            {
                if (person == null) return null;

            return new PersonRespones
            {
                PersonId = person.PersonId,
                Name = person.Name,
                DateOfBirth = person.DateOfBirth,
                email = person.email,
                phone = person.phone,
                Gender = person.Gender,
                Address = person.Address,
                CountryId = person.CountryId,
                Age = person.DateOfBirth.HasValue ? DateTime.Now.Year - person.DateOfBirth.Value.Year : 0,
                CountryName = person.Country?.CountryName,
            };
            }
        }
    }






