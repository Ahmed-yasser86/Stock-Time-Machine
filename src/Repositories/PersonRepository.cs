using Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RepositryContracts;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SerilogTimings;

namespace Repositories
{
    public class PersonRepository : PersonRepositryContract
    {
        private readonly AppDBContext _db;
        private readonly ILogger<PersonRepository> _logger;

        public PersonRepository(AppDBContext db, ILogger<PersonRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Person> AddPerson(Person person)
        {
            using (Operation.Time("AddPerson database operation for Person: {PersonName}", person?.Name))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Person: {@Person}",
                    nameof(AddPerson), DateTime.UtcNow, person);

                try
                {
                    if (person == null)
                    {
                        _logger.LogWarning("AddPerson called with null person parameter");
                        throw new ArgumentNullException(nameof(person));
                    }

                    _logger.LogDebug("Adding person with ID: {PersonId}, Name: {PersonName} to database",
                        person.PersonId, person.Name);

                    await _db.Persons.AddAsync(person);
                    await _db.SaveChangesAsync();

                    _logger.LogInformation("Successfully added person with ID: {PersonId}, Name: {PersonName}",
                        person.PersonId, person.Name);

                    return person;
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Database update error while adding person. Person: {@Person}. Error: {ErrorMessage}",
                        person, ex.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in {MethodName} for person: {@Person}",
                        nameof(AddPerson), person);
                    throw;
                }
            }
        }

        public async Task<bool> DeletePerson(Guid? id)
        {
            using (Operation.Time("DeletePerson database operation for ID: {PersonId}", id))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Person ID: {PersonId}",
                    nameof(DeletePerson), DateTime.UtcNow, id);

                try
                {
                    if (id == null)
                    {
                        _logger.LogWarning("DeletePerson called with null ID");
                        return false;
                    }

                    _logger.LogDebug("Searching for person with ID: {PersonId}", id);
                    var person = await _db.Persons.FindAsync(id);

                    if (person == null)
                    {
                        _logger.LogInformation("No person found to delete with ID: {PersonId}", id);
                        return false;
                    }

                    _logger.LogDebug("Found person with ID: {PersonId}, Name: {PersonName}. Removing from database.",
                        person.PersonId, person.Name);

                    _db.Persons.Remove(person);
                    await _db.SaveChangesAsync();

                    _logger.LogInformation("Successfully deleted person with ID: {PersonId}, Name: {PersonName}",
                        person.PersonId, person.Name);

                    return true;
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Database update error while deleting person with ID: {PersonId}", id);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in {MethodName} for Person ID: {PersonId}",
                        nameof(DeletePerson), id);
                    throw;
                }
            }
        }

        public async Task<IEnumerable<Person>> GetAllPersons()
        {
            using (Operation.Time("GetAllPersons database operation with Include"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetAllPersons), DateTime.UtcNow);

                try
                {
                    _logger.LogDebug("Retrieving all persons with country data (using Include)");
                    var persons = await _db.Persons.Include(c => c.Country).ToListAsync();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} persons (with country data)",
                        nameof(GetAllPersons), persons.Count);

                    return persons;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method", nameof(GetAllPersons));
                    throw;
                }
            }
        }

        public async Task<List<Person?>> GetFilteredPersons(Expression<Func<Person, bool>> predicate)
        {
            using (Operation.Time("GetFilteredPersons database operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetFilteredPersons), DateTime.UtcNow);

                try
                {
                    if (predicate == null)
                    {
                        _logger.LogWarning("GetFilteredPersons called with null predicate");
                        throw new ArgumentNullException(nameof(predicate));
                    }

                    _logger.LogDebug("Executing filtered query on Persons with country data (using Include)");
                    var persons = await _db.Persons.Include(c => c.Country).Where(predicate).ToListAsync();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} persons matching filter",
                        nameof(GetFilteredPersons), persons.Count);

                    // Log the predicate expression for debugging (can be verbose, consider using Debug level)
                    _logger.LogDebug("Filter predicate used: {Predicate}", predicate.ToString());

                    return persons;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method with predicate: {Predicate}",
                        nameof(GetFilteredPersons), predicate?.ToString());
                    throw;
                }
            }
        }

        public async Task<Person?> GetPersonById(Guid? id)
        {
            using (Operation.Time("GetPersonById database query for ID: {PersonId}", id))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Person ID: {PersonId}",
                    nameof(GetPersonById), DateTime.UtcNow, id);

                try
                {
                    if (id == null)
                    {
                        _logger.LogWarning("GetPersonById called with null ID");
                        return null;
                    }

                    _logger.LogDebug("Retrieving person with ID: {PersonId}", id);
                    var person = await _db.Persons.FindAsync(id);

                    if (person == null)
                    {
                        _logger.LogInformation("No person found with ID: {PersonId}", id);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully retrieved person with ID: {PersonId}, Name: {PersonName}",
                            person.PersonId, person.Name);
                    }

                    return person;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} for Person ID: {PersonId}",
                        nameof(GetPersonById), id);
                    throw;
                }
            }
        }

        public async Task<Person> UpdatePerson(Person person)
        {
            using (Operation.Time("UpdatePerson database operation for Person ID: {PersonId}", person?.PersonId))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Person: {@Person}",
                    nameof(UpdatePerson), DateTime.UtcNow, person);

                try
                {
                    if (person == null)
                    {
                        _logger.LogWarning("UpdatePerson called with null person parameter");
                        throw new ArgumentNullException(nameof(person));
                    }

                    if (person.PersonId == Guid.Empty)
                    {
                        _logger.LogWarning("UpdatePerson called with person having empty GUID");
                        throw new ArgumentException("Person ID cannot be empty", nameof(person.PersonId));
                    }

                    _logger.LogDebug("Updating person with ID: {PersonId}, Name: {PersonName}",
                        person.PersonId, person.Name);

                    // Check if person exists before update
                    var existingPerson = await _db.Persons.FindAsync(person.PersonId);
                    if (existingPerson == null)
                    {
                        _logger.LogWarning("Attempted to update non-existent person with ID: {PersonId}", person.PersonId);
                        throw new InvalidOperationException($"Person with ID {person.PersonId} does not exist.");
                    }

                    _db.Persons.Update(person);
                    await _db.SaveChangesAsync();

                    _logger.LogInformation("Successfully updated person with ID: {PersonId}, Name: {PersonName}",
                        person.PersonId, person.Name);

                    return person;
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Database update error while updating person. Person: {@Person}. Error: {ErrorMessage}",
                        person, ex.Message);
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Invalid operation while updating person: {@Person}", person);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in {MethodName} for person: {@Person}",
                        nameof(UpdatePerson), person);
                    throw;
                }
            }
        }
    }
}