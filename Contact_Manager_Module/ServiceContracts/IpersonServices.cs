using ServiceContracts.DTOs;
using ServiceContracts.DTOs.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceContracts
{
    public interface IPersonServices
    {

        public  Task<PersonRespones> AddPerson(PersonAddRequest? personAddRequest);
        public  Task<List<PersonRespones>> GetAllPersons();

        public  Task<PersonRespones?> GetPersonByPersonId(Guid? personId);

        public  Task<List<PersonRespones>> getPersonsSorted(List<PersonRespones> persons, string? sortBy, sortedListOp sortOrder);
        public  Task<List<PersonRespones>> SearchPersonsBy(string? SearchBy,string SearchString);
        public  Task<PersonRespones?> UpdatePerson(PersonUpdateRequest? personUpdateRequest);

     public   Task<bool> DeletePersonByPersonId(Guid? personId);
//object GetPersonByPersonId(int id);
    }
}
