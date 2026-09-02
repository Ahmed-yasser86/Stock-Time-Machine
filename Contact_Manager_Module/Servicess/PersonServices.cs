using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceContracts;
using ServiceContracts.DTOs;
using ServiceContracts.DTOs.Enums;
using Servicess.Helpers;
using RepositryContracts;
using Repositories;
using SerilogTimings;

namespace Servicess
{
    public class PersonServices : IPersonServices
    {
        private readonly PersonRepositryContract PersonRipository;
        private readonly ICountryServices _countryServices;
        private readonly ILogger<PersonServices> _logger;

        public PersonServices(PersonRepositryContract personRipository, ILogger<PersonServices> logger)
        {
            PersonRipository = personRipository;
            _logger = logger;
        }

        public async Task<PersonRespones> AddPerson(PersonAddRequest? personAddRequest)
        {
            using (Operation.Time("Add person operation for: {PersonName}", personAddRequest?.Name ?? "null"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Request data: {@PersonAddRequest}",
                    nameof(AddPerson), DateTime.UtcNow, personAddRequest);

                try
                {
                    if (personAddRequest == null)
                    {
                        _logger.LogWarning("AddPerson called with null request parameter");
                        throw new ArgumentNullException(nameof(personAddRequest));
                    }

                    _logger.LogDebug("Validating person add request");
                    ValidationHelpers.ValidationFunction(personAddRequest);

                    _logger.LogDebug("Converting PersonAddRequest to Person entity");
                    var Person = personAddRequest.ToPerson();
                    Person.PersonId = Guid.NewGuid();

                    _logger.LogDebug("Adding new person with ID: {PersonId}, Name: {PersonName}",
                        Person.PersonId, Person.Name);

                    await PersonRipository.AddPerson(Person);

                    var PersonResponsType = Person.ConvertToPersonRespons();
                    PersonResponsType.CountryName = Person.Country?.CountryName;

                    _logger.LogInformation("Successfully added new person. ID: {PersonId}, Name: {PersonName}, Country: {CountryName}",
                        PersonResponsType.PersonId, PersonResponsType.Name, PersonResponsType.CountryName);

                    return PersonResponsType;
                }
                catch (ValidationException ex)
                {
                    _logger.LogWarning(ex, "Validation error in AddPerson for request: {@PersonAddRequest}", personAddRequest);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in AddPerson for request: {@PersonAddRequest}", personAddRequest);
                    throw;
                }
            }
        }

        public async Task<bool> DeletePersonByPersonId(Guid? personId)
        {
            using (Operation.Time("Delete person operation for ID: {PersonId}", personId))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. PersonId: {PersonId}",
                    nameof(DeletePersonByPersonId), DateTime.UtcNow, personId);

                try
                {
                    if (personId == Guid.Empty || personId == null)
                    {
                        _logger.LogWarning("DeletePersonByPersonId called with invalid PersonId: {PersonId}", personId);
                        return false;
                    }

                    _logger.LogDebug("Attempting to delete person with ID: {PersonId}", personId);
                    var result = await PersonRipository.DeletePerson(personId);

                    if (result)
                    {
                        _logger.LogInformation("Successfully deleted person with ID: {PersonId}", personId);
                    }
                    else
                    {
                        _logger.LogWarning("No person found to delete with ID: {PersonId}", personId);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in DeletePersonByPersonId for PersonId: {PersonId}", personId);
                    throw;
                }
            }
        }

        public async Task<List<PersonRespones>> GetAllPersons()
        {
            using (Operation.Time("Get all persons operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetAllPersons), DateTime.UtcNow);

                try
                {
                    var list = await PersonRipository.GetAllPersons();
                    var result = list.Select(p => p.ConvertToPersonRespons()).ToList();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} persons",
                        nameof(GetAllPersons), result.Count);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method", nameof(GetAllPersons));
                    throw;
                }
            }
        }

        public async Task<PersonRespones?> GetPersonByPersonId(Guid? personId)
        {
            using (Operation.Time("Get person by ID operation for: {PersonId}", personId))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. PersonId: {PersonId}",
                    nameof(GetPersonByPersonId), DateTime.UtcNow, personId);

                try
                {
                    if (personId == Guid.Empty || personId == null)
                    {
                        _logger.LogWarning("GetPersonByPersonId called with invalid PersonId: {PersonId}", personId);
                        return null;
                    }

                    _logger.LogDebug("Retrieving person with ID: {PersonId}", personId);
                    Person? person = await PersonRipository.GetPersonById(personId);

                    if (person == null)
                    {
                        _logger.LogWarning("No person found with ID: {PersonId}", personId);
                        return null;
                    }

                    var result = person.ConvertToPersonRespons();
                    _logger.LogInformation("Successfully retrieved person with ID: {PersonId}, Name: {PersonName}",
                        result.PersonId, result.Name);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in GetPersonByPersonId for PersonId: {PersonId}", personId);
                    throw;
                }
            }
        }

        public async Task<List<PersonRespones>> getPersonsSorted(List<PersonRespones> persons, string? sortBy, sortedListOp sortOrder)
        {
            using (Operation.Time("Sort {Count} persons by {SortBy} ({SortOrder})", persons?.Count ?? 0, sortBy ?? "none", sortOrder))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. SortBy: {SortBy}, SortOrder: {SortOrder}, PersonsCount: {PersonsCount}",
                    nameof(getPersonsSorted), DateTime.UtcNow, sortBy, sortOrder, persons?.Count ?? 0);

                try
                {
                    if (string.IsNullOrEmpty(sortBy))
                    {
                        _logger.LogDebug("No sort criteria provided, returning unsorted list");
                        return persons;
                    }

                    List<PersonRespones> sortedPersons = (sortOrder, sortOrder) switch
                    {
                        (sortedListOp.Ascending, _) => sortBy switch
                        {
                            nameof(PersonRespones.Name) => persons.OrderBy(p => p.Name).ToList(),
                            nameof(PersonRespones.email) => persons.OrderBy(p => p.email).ToList(),
                            nameof(PersonRespones.phone) => persons.OrderBy(p => p.phone).ToList(),
                            nameof(PersonRespones.DateOfBirth) => persons.OrderBy(p => p.DateOfBirth).ToList(),
                            nameof(PersonRespones.CountryName) => persons.OrderBy(p => p.CountryName).ToList(),
                            nameof(PersonRespones.Age) => persons.OrderBy(p => p.Age).ToList(),
                            nameof(PersonRespones.PersonId) => persons.OrderBy(p => p.PersonId).ToList(),
                            nameof(PersonRespones.Address) => persons.OrderBy(p => p.Address).ToList(),
                            _ => persons
                        },
                        (sortedListOp.Descending, _) => sortBy switch
                        {
                            nameof(PersonRespones.Name) => persons.OrderByDescending(p => p.Name).ToList(),
                            nameof(PersonRespones.email) => persons.OrderByDescending(p => p.email).ToList(),
                            nameof(PersonRespones.phone) => persons.OrderByDescending(p => p.phone).ToList(),
                            nameof(PersonRespones.DateOfBirth) => persons.OrderByDescending(p => p.DateOfBirth).ToList(),
                            nameof(PersonRespones.CountryName) => persons.OrderByDescending(p => p.CountryName).ToList(),
                            nameof(PersonRespones.Age) => persons.OrderByDescending(p => p.Age).ToList(),
                            nameof(PersonRespones.PersonId) => persons.OrderByDescending(p => p.PersonId).ToList(),
                            nameof(PersonRespones.Address) => persons.OrderByDescending(p => p.Address).ToList(),
                            _ => persons
                        },
                        _ => persons
                    };

                    _logger.LogInformation("{MethodName} completed successfully. Sorted {Count} persons by {SortBy} ({SortOrder})",
                        nameof(getPersonsSorted), sortedPersons.Count, sortBy, sortOrder);

                    return sortedPersons;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method. SortBy: {SortBy}, SortOrder: {SortOrder}",
                        nameof(getPersonsSorted), sortBy, sortOrder);
                    throw;
                }
            }
        }

        public async Task<List<PersonRespones>> SearchPersonsBy(string? PersonParamter, string SearchBy)
        {
            using (Operation.Time("Search persons by {SearchBy} with parameter: {Parameter}", SearchBy ?? "none", PersonParamter ?? "null"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. SearchBy: {SearchBy}, Parameter: {Parameter}",
                    nameof(SearchPersonsBy), DateTime.UtcNow, SearchBy, PersonParamter);

                try
                {
                    List<PersonRespones> MatchingResults = new List<PersonRespones>();

                    switch (SearchBy)
                    {
                        case nameof(PersonRespones.Name):
                            {
                                _logger.LogDebug("Searching persons by Name containing: {Parameter}", PersonParamter);
                                var filteredPersons = await PersonRipository.GetFilteredPersons(p => p.Name != null && p.Name.Contains(PersonParamter));
                                MatchingResults = filteredPersons
                                    .Where(p => p != null)
                                    .Select(p => p!.ConvertToPersonRespons())
                                    .ToList();
                                break;
                            }
                        case nameof(PersonRespones.email):
                            {
                                _logger.LogDebug("Searching persons by Email containing: {Parameter}", PersonParamter);
                                var filteredPersons = await PersonRipository.GetFilteredPersons(p => p.email != null && p.email.Contains(PersonParamter));
                                MatchingResults = filteredPersons
                                    .Where(p => p != null)
                                    .Select(p => p!.ConvertToPersonRespons())
                                    .ToList();
                                break;
                            }
                        case nameof(PersonRespones.phone):
                            {
                                _logger.LogDebug("Searching persons by Phone containing: {Parameter}", PersonParamter);
                                var filteredPersons = await PersonRipository.GetFilteredPersons(p => p.phone != null && p.phone.Contains(PersonParamter));
                                MatchingResults = filteredPersons
                                    .Where(p => p != null)
                                    .Select(p => p!.ConvertToPersonRespons())
                                    .ToList();
                                break;
                            }
                        case nameof(PersonRespones.DateOfBirth):
                            {
                                _logger.LogDebug("Searching persons by DateOfBirth containing: {Parameter}", PersonParamter);
                                var filteredPersons = await PersonRipository.GetFilteredPersons(p => p.DateOfBirth != null && p.DateOfBirth.Value.ToString("yyyy-MM-dd").Contains(PersonParamter));
                                MatchingResults = filteredPersons
                                    .Where(p => p != null)
                                    .Select(p => p!.ConvertToPersonRespons())
                                    .ToList();
                                break;
                            }
                        default:
                            {
                                _logger.LogWarning("Unknown search criteria: {SearchBy}. Returning all persons.", SearchBy);
                                var Persons = await PersonRipository.GetAllPersons();
                                MatchingResults = Persons
                                    .Select(p => p!.ConvertToPersonRespons())
                                    .ToList();
                                break;
                            }
                    }

                    _logger.LogInformation("{MethodName} completed successfully. Found {Count} results for SearchBy: {SearchBy}, Parameter: {Parameter}",
                        nameof(SearchPersonsBy), MatchingResults.Count, SearchBy, PersonParamter);

                    return MatchingResults;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method. SearchBy: {SearchBy}, Parameter: {Parameter}",
                        nameof(SearchPersonsBy), SearchBy, PersonParamter);
                    throw;
                }
            }
        }

        public async Task<PersonRespones?> UpdatePerson(PersonUpdateRequest? personUpdateRequest)
        {
            using (Operation.Time("Update person operation for ID: {PersonId}", personUpdateRequest?.PersonId))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Request data: {@PersonUpdateRequest}",
                    nameof(UpdatePerson), DateTime.UtcNow, personUpdateRequest);

                try
                {
                    if (personUpdateRequest == null)
                    {
                        _logger.LogWarning("UpdatePerson called with null request parameter");
                        throw new ArgumentNullException(nameof(personUpdateRequest));
                    }

                    _logger.LogDebug("Validating person update request");
                    ValidationHelpers.ValidationFunction(personUpdateRequest);

                    _logger.LogDebug("Retrieving existing person with ID: {PersonId}", personUpdateRequest.PersonId);
                    var person = await PersonRipository?.GetPersonById(personUpdateRequest.PersonId);

                    if (person == null)
                    {
                        _logger.LogWarning("Attempted to update non-existent person with ID: {PersonId}", personUpdateRequest.PersonId);
                        throw new ArgumentException("Given person ID does not exist.");
                    }

                    _logger.LogDebug("Updating person fields for ID: {PersonId}", personUpdateRequest.PersonId);

                    // Log each field that is being updated
                    if (personUpdateRequest.Name != null && person.Name != personUpdateRequest.Name)
                        _logger.LogDebug("Updating Name from '{OldValue}' to '{NewValue}'", person.Name, personUpdateRequest.Name);

                    if (personUpdateRequest.DateOfBirth != null && person.DateOfBirth != personUpdateRequest.DateOfBirth)
                        _logger.LogDebug("Updating DateOfBirth from '{OldValue}' to '{NewValue}'", person.DateOfBirth, personUpdateRequest.DateOfBirth);

                    if (personUpdateRequest.email != null && person.email != personUpdateRequest.email)
                        _logger.LogDebug("Updating Email from '{OldValue}' to '{NewValue}'", person.email, personUpdateRequest.email);

                    if (personUpdateRequest.phone != null && person.phone != personUpdateRequest.phone)
                        _logger.LogDebug("Updating Phone from '{OldValue}' to '{NewValue}'", person.phone, personUpdateRequest.phone);

                    if (personUpdateRequest.Address != null && person.Address != personUpdateRequest.Address)
                        _logger.LogDebug("Updating Address from '{OldValue}' to '{NewValue}'", person.Address, personUpdateRequest.Address);

                    person.Name = personUpdateRequest.Name ?? person.Name;
                    person.DateOfBirth = personUpdateRequest.DateOfBirth ?? person.DateOfBirth;
                    person.email = personUpdateRequest.email ?? person.email;
                    person.phone = personUpdateRequest.phone ?? person.phone;
                    person.NewsLetter = personUpdateRequest.NewsLetter ?? person.NewsLetter;
                    person.Address = personUpdateRequest.Address ?? person.Address;
                    person.CountryId = personUpdateRequest.CountryId ?? person.CountryId;
                    person.Gender = personUpdateRequest.Gender.ToString() ?? person.Gender;

                    await PersonRipository.UpdatePerson(person);

                    var result = person.ConvertToPersonRespons();

                    _logger.LogInformation("Successfully updated person with ID: {PersonId}, Name: {PersonName}",
                        result.PersonId, result.Name);

                    return result;
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "Argument error in UpdatePerson for PersonId: {PersonId}",
                        personUpdateRequest?.PersonId);
                    throw;
                }
                catch (ValidationException ex)
                {
                    _logger.LogWarning(ex, "Validation error in UpdatePerson for request: {@PersonUpdateRequest}", personUpdateRequest);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in UpdatePerson for request: {@PersonUpdateRequest}", personUpdateRequest);
                    throw;
                }
            }
        }
    }
}