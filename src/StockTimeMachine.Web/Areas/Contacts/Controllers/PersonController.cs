using Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServiceContracts;
using ServiceContracts.DTOs;
using ServiceContracts.DTOs.Enums;
using System.Threading.Tasks;
using SerilogTimings;

namespace StocksApp2.ContactComponent.Controllers
{
    [Area("Contacts")]
    public class PersonController : Controller
    {
        private readonly IPersonServices _personServices;
        private readonly ICountryServices _countryServices;
        private readonly ILogger<PersonController> _logger;

        public PersonController(IPersonServices personServices, ICountryServices countryServices, ILogger<PersonController> logger)
        {
            _personServices = personServices;
            _countryServices = countryServices;
            _logger = logger;
        }

        [Route("Person/Create")]
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            using (Operation.Time("Create GET action execution"))
            {
                _logger.LogInformation("Executing {ActionName} GET method at {Timestamp}",
                    nameof(Create), DateTime.UtcNow);

                try
                {
                    List<CountryResponse> countries;

                    using (Operation.Time("Loading countries for create form"))
                    {
                        _logger.LogDebug("Loading countries for create form");
                        countries = await _countryServices.Countries();
                        ViewBag.Countries = countries;
                    }

                    _logger.LogInformation("Create form loaded successfully with {Count} countries", countries.Count);
                    return View();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while loading create form");
                    return View("Error");
                }
            }
        }

        [Route("Person/Create")]
        [HttpPost]
        public async Task<IActionResult> Create(PersonAddRequest model)
        {
            using (Operation.Time("Create POST action execution"))
            {
                _logger.LogInformation("Executing {ActionName} POST method at {Timestamp}. Model: {@Model}",
                    nameof(Create), DateTime.UtcNow, model);

                try
                {
                    using (Operation.Time("Loading countries for validation"))
                    {
                        _logger.LogDebug("Loading countries for create form validation");
                        List<CountryResponse> countries = await _countryServices.Countries();
                        ViewBag.Countries = countries;
                    }

                    if (ModelState.IsValid)
                    {
                        using (Operation.Time("Adding person to database"))
                        {
                            _logger.LogDebug("Model is valid. Attempting to add person: {@Model}", model);
                            await _personServices.AddPerson(model);
                        }

                        _logger.LogInformation("Person added successfully. Redirecting to Index");
                        return RedirectToAction("Index", "Person");
                    }

                    var modelErrors = ModelState.Values.SelectMany(v => v.Errors);
                    _logger.LogWarning("Model validation failed for create. Errors: {Errors}",
                        string.Join("; ", modelErrors.Select(e => e.ErrorMessage)));

                    ViewBag.ModelErrors = modelErrors;
                    return View(model);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while creating person. Model: {@Model}", model);
                    return View("Error");
                }
            }
        }

        [HttpGet]
        [Route("Person/Edit/{id}")]
        public async Task<IActionResult> Edit(Guid id)
        {
            using (Operation.Time("Edit GET action execution for ID: {PersonId}", id))
            {
                _logger.LogInformation("Executing {ActionName} GET method at {Timestamp}. Person ID: {PersonId}",
                    nameof(Edit), DateTime.UtcNow, id);

                try
                {
                    using (Operation.Time("Loading countries for edit form"))
                    {
                        _logger.LogDebug("Loading countries for edit form");
                        List<CountryResponse> countries = await _countryServices.Countries();
                        ViewBag.Countries = countries;
                    }

                    PersonRespones? person;

                    using (Operation.Time("Retrieving person by ID: {PersonId}", id))
                    {
                        _logger.LogDebug("Retrieving person with ID: {PersonId}", id);
                        person = await _personServices.GetPersonByPersonId(id);
                    }

                    if (person == null)
                    {
                        _logger.LogWarning("Person not found for edit with ID: {PersonId}", id);
                        return NotFound();
                    }

                    PersonUpdateRequest? personUpdate = person?.ToPersonUpdateRequest();

                    _logger.LogInformation("Edit form loaded successfully for person: {PersonId}, Name: {PersonName}",
                        person.PersonId, person.Name);

                    return View(personUpdate);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while loading edit form for Person ID: {PersonId}", id);
                    return View("Error");
                }
            }
        }

        [HttpPost]
        [Route("Person/Edit/{id}")]
        public async Task<IActionResult> Edit(PersonUpdateRequest model)
        {
            using (Operation.Time("Edit POST action execution for Person ID: {PersonId}", model.PersonId))
            {
                _logger.LogInformation("Executing {ActionName} POST method at {Timestamp}. Model: {@Model}",
                    nameof(Edit), DateTime.UtcNow, model);

                try
                {
                    using (Operation.Time("Loading countries for edit validation"))
                    {
                        _logger.LogDebug("Loading countries for edit form validation");
                        List<CountryResponse> countries = await _countryServices.Countries();
                        ViewBag.Countries = countries;
                    }

                    if (ModelState.IsValid)
                    {
                        PersonRespones? updatedPerson;

                        using (Operation.Time("Updating person in database"))
                        {
                            _logger.LogDebug("Model is valid. Attempting to update person with ID: {PersonId}", model.PersonId);
                            updatedPerson = await _personServices.UpdatePerson(model);
                        }

                        if (updatedPerson == null)
                        {
                            _logger.LogWarning("Person not found for update with ID: {PersonId}", model.PersonId);
                            return NotFound();
                        }

                        _logger.LogInformation("Person updated successfully. ID: {PersonId}, Name: {PersonName}",
                            updatedPerson.PersonId, updatedPerson.Name);

                        return RedirectToAction("Index", "Person");
                    }

                    var modelErrors = ModelState.Values.SelectMany(v => v.Errors);
                    _logger.LogWarning("Model validation failed for edit. Person ID: {PersonId}. Errors: {Errors}",
                        model.PersonId, string.Join("; ", modelErrors.Select(e => e.ErrorMessage)));

                    ViewBag.ModelErrors = modelErrors;
                    return View(model);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while updating person. Model: {@Model}", model);
                    return View("Error");
                }
            }
        }

        [HttpGet]
        [Route("Person/Delete/{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            using (Operation.Time("Delete GET action execution for ID: {PersonId}", id))
            {
                _logger.LogInformation("Executing {ActionName} GET method at {Timestamp}. Person ID: {PersonId}",
                    nameof(Delete), DateTime.UtcNow, id);

                try
                {
                    var person = await _personServices.GetPersonByPersonId(id);

                    if (person == null)
                    {
                        _logger.LogWarning("Person not found for delete with ID: {PersonId}. Redirecting to Index", id);
                        return RedirectToAction("Index", "Person");
                    }

                    _logger.LogInformation("Delete confirmation form loaded for person: {PersonId}, Name: {PersonName}",
                        person.PersonId, person.Name);

                    return View(person);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while loading delete confirmation for Person ID: {PersonId}", id);
                    return View("Error");
                }
            }
        }

        [HttpPost]
        [Route("Person/Delete/{id}")]
        public async Task<IActionResult> Delete(PersonRespones model)
        {
            using (Operation.Time("Delete POST action execution for Person ID: {PersonId}", model.PersonId))
            {
                _logger.LogInformation("Executing {ActionName} POST method at {Timestamp}. Person ID: {PersonId}, Name: {PersonName}",
                    nameof(Delete), DateTime.UtcNow, model.PersonId, model.Name);

                try
                {
                    bool isDeleted;

                    using (Operation.Time("Deleting person from database"))
                    {
                        _logger.LogDebug("Attempting to delete person with ID: {PersonId}", model.PersonId);
                        isDeleted = await _personServices.DeletePersonByPersonId(model.PersonId);
                    }

                    if (isDeleted)
                    {
                        _logger.LogInformation("Person deleted successfully. ID: {PersonId}, Name: {PersonName}",
                            model.PersonId, model.Name);
                    }
                    else
                    {
                        _logger.LogWarning("Person deletion failed. Person not found with ID: {PersonId}",
                            model.PersonId);
                    }

                    return RedirectToAction("Index", "Person");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while deleting person. Model: {@Model}", model);
                    return View("Error");
                }
            }
        }

        [Route("Person/index")]
        [Route("/")]
        public async Task<IActionResult> Index(string SearchBy, string SearchString,
            string SortedBy, sortedListOp SortOption = sortedListOp.Ascending)
        {
            using (Operation.Time("Index action execution - SearchBy: {SearchBy}, SearchString: {SearchString}, SortedBy: {SortedBy}, SortOption: {SortOption}",
                SearchBy, SearchString, SortedBy, SortOption))
            {
                _logger.LogInformation("Executing {ActionName} method at {Timestamp}. Parameters - SearchBy: {SearchBy}, SearchString: {SearchString}, SortedBy: {SortedBy}, SortOption: {SortOption}",
                    nameof(Index), DateTime.UtcNow, SearchBy, SearchString, SortedBy, SortOption);

                try
                {
                    ViewBag.SearchFields = new Dictionary<string, string>()
                    {
                        {nameof(Person.email), "Email"},
                        {nameof(Person.Gender), "Gender"},
                        {nameof(Person.NewsLetter), "News Letter"},
                        {nameof(Person.DateOfBirth), "Date Of Birth"},
                        {nameof(Person.phone), "Phone"},
                        {nameof(Person.CountryId), "Country"},
                        {nameof(Person.Name), "Name"}
                    };

                    ViewBag.CurrentSearchBy = SearchBy;
                    ViewBag.CurrentSearchString = SearchString;
                    ViewBag.CurrentSortedBy = SortedBy;
                    ViewBag.CurrentSortOption = SortOption.ToString();

                    List<PersonRespones> persons;

                    using (Operation.Time("Searching persons with criteria"))
                    {
                        _logger.LogDebug("Searching persons with criteria - SearchBy: {SearchBy}, SearchString: {SearchString}",
                            SearchBy, SearchString);
                        persons = await _personServices.SearchPersonsBy(SearchString, SearchBy);
                    }

                    List<PersonRespones> SortedPersons;

                    using (Operation.Time("Sorting {Count} persons", persons.Count))
                    {
                        _logger.LogDebug("Sorting {Count} persons by {SortedBy} ({SortOption})",
                            persons.Count, SortedBy, SortOption);
                        SortedPersons = await _personServices.getPersonsSorted(persons, SortedBy, SortOption);
                    }

                    _logger.LogInformation("Index page loaded successfully. Retrieved {Count} persons",
                        SortedPersons.Count);

                    return View(SortedPersons);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in Index action. SearchBy: {SearchBy}, SearchString: {SearchString}, SortedBy: {SortedBy}, SortOption: {SortOption}",
                        SearchBy, SearchString, SortedBy, SortOption);
                    return View("Error");
                }
            }
        }
    }
}