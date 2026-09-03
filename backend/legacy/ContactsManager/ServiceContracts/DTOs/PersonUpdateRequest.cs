using Entities;
using ServiceContracts.DTOs.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceContracts.DTOs
{
    public class PersonUpdateRequest
    {

        [Required]
        public Guid? PersonId { get; set; }

        [Required(ErrorMessage = "Name is required")]
        [StringLength(40, ErrorMessage = "Name cannot exceed 40 characters")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Date of Birth is required")]
        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string? email { get; set; }

        [Phone(ErrorMessage = "Invalid phone number format")]
        public string? phone { get; set; }

        [Required(ErrorMessage = "Gender is required")]
        public GenderOptions? Gender { get; set; }

        [StringLength(200, ErrorMessage = "Address is too long")]
        public string? Address { get; set; }

        [Required(ErrorMessage = "Please select a country")]
        public Guid? CountryId { get; set; }

        public bool? NewsLetter { get; set; }

        public Person ToPerson()
        {
            return new Person
            {
                Name = this.Name,
                DateOfBirth = this.DateOfBirth,
                email = this.email,
                phone = this.phone,
                Gender = this.Gender.ToString(),
                Address = this.Address,
                CountryId = (Guid)this.CountryId,
                NewsLetter = this.NewsLetter

            };

        }
    }
}

