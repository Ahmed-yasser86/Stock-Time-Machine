using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Entities
{

    public class Person
    {
        [Key]
        public Guid PersonId { get; set; }
        [StringLength(100)]
        public string Name { get; set; }

        public DateTime ?DateOfBirth { get; set; }
        [EmailAddress]  
        public string? email { get; set; }
        
        public string phone { get; set; }

        [StringLength(10)]
        public string Gender { get; set; }

        public bool ?NewsLetter { get; set; }

        [StringLength(200)]
        public string ?Address { get; set; }
        public Guid CountryId { get; set; }

        [ForeignKey("CountryId")]
        public Country? Country { get; set; }
    }
}
