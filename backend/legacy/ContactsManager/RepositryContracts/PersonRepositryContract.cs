using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Entities;
namespace RepositryContracts
{
    public interface PersonRepositryContract
    {

        Task<Person> AddPerson(Person person);

        Task<Person> UpdatePerson(Person person);

        Task<Person>? GetPersonById(Guid? id);

        Task<IEnumerable<Person>> GetAllPersons();

        Task<List<Person?>> GetFilteredPersons(Expression<Func<Person, bool>> predicate);

        Task<bool> DeletePerson(Guid? id);

    }
}
