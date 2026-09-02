using AutoFixture;
using Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceContracts;
using ServiceContracts.DTOs;
using ServiceContracts.DTOs.Enums;
using StocksApp2.ContactComponent.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CRUDTests.ControllersTest
{
    public class PersonControllerTests
    {
        private readonly PersonController _personController;
        private readonly Mock<IPersonServices> _personServicesMock;
        private readonly Mock<ICountryServices> _countryServicesMock;
        private readonly Fixture _fixture;
        private readonly Mock<ILogger<PersonController>> _controllerLoggerMock;
        private readonly ILogger<PersonControllerTests> _testLogger;

        public PersonControllerTests()
        {
            // Use NullLogger if you don't need actual logging in tests
            _testLogger = NullLogger<PersonControllerTests>.Instance;

            _fixture = new Fixture();

            // Configure AutoFixture to handle recursion
            _fixture.Behaviors.OfType<ThrowingRecursionBehavior>()
                .ToList()
                .ForEach(b => _fixture.Behaviors.Remove(b));
            _fixture.Behaviors.Add(new OmitOnRecursionBehavior());

            // Initialize mocks
            _personServicesMock = new Mock<IPersonServices>();
            _countryServicesMock = new Mock<ICountryServices>();
            _controllerLoggerMock = new Mock<ILogger<PersonController>>();

            // Create controller with all dependencies
            _personController = new PersonController(
                _personServicesMock.Object,
                _countryServicesMock.Object,
                _controllerLoggerMock.Object
            );
        }

        #region Create (GET)

        [Fact]
        public async Task Create_Get_ShouldReturnViewWithCountries()
        {
            // Arrange
            List<CountryResponse> countries = _fixture.Create<List<CountryResponse>>();

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            // Act
            IActionResult result = await _personController.Create();

            // Assert
            ViewResult viewResult = Assert.IsType<ViewResult>(result);

            // Check ViewBag contains countries
            var viewBagCountries = _personController.ViewBag.Countries;
            Assert.Equal(countries, viewBagCountries);

            // Verify service was called
            _countryServicesMock.Verify(x => x.Countries(), Times.Once);
        }

        #endregion

        #region Create (POST)

        [Fact]
        public async Task Create_Post_ValidModel_ShouldRedirectToIndex()
        {
            // Arrange
            PersonAddRequest model = _fixture.Create<PersonAddRequest>();
            List<CountryResponse> countries = _fixture.Create<List<CountryResponse>>();

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            _personServicesMock
                .Setup(x => x.AddPerson(model))
                .ReturnsAsync(_fixture.Create<PersonRespones>());

            // Act
            IActionResult result = await _personController.Create(model);

            // Assert
            RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Person", redirectResult.ControllerName);

            _personServicesMock.Verify(x => x.AddPerson(model), Times.Once);
        }

        [Fact]
        public async Task Create_Post_InvalidModel_ShouldReturnViewWithModel()
        {
            // Arrange
            PersonAddRequest model = _fixture.Create<PersonAddRequest>();
            List<CountryResponse> countries = _fixture.Create<List<CountryResponse>>();

            _personController.ModelState.AddModelError("Name", "Name is required");

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            // Act
            IActionResult result = await _personController.Create(model);

            // Assert
            ViewResult viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal(model, viewResult.Model);

            // Verify ViewBag contains errors
            var modelErrors = _personController.ViewBag.ModelErrors;
            Assert.NotNull(modelErrors);

            _personServicesMock.Verify(x => x.AddPerson(It.IsAny<PersonAddRequest>()), Times.Never);
        }

        #endregion

        #region Edit (GET)

        [Fact]
        public async Task Edit_Get_WithValidId_ShouldReturnViewWithPerson()
        {
            // Arrange
            Guid personId = Guid.NewGuid();
            PersonRespones person = _fixture.Build<PersonRespones>()
                .With(p => p.Gender, "Male")
                .With(p => p.email, _fixture.Create<string>() + "@example.com")
                .Create();
            List<CountryResponse> countries = _fixture.Create<List<CountryResponse>>();

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            _personServicesMock
                .Setup(x => x.GetPersonByPersonId(personId))
                .ReturnsAsync(person);

            // Act
            IActionResult result = await _personController.Edit(personId);

            // Assert
            ViewResult viewResult = Assert.IsType<ViewResult>(result);
            PersonUpdateRequest? model = Assert.IsType<PersonUpdateRequest>(viewResult.Model);

            Assert.Equal(person.PersonId, model.PersonId);
            Assert.Equal(person.Name, model.Name);

            _personServicesMock.Verify(x => x.GetPersonByPersonId(personId), Times.Once);
        }

        [Fact]
        public async Task Edit_Get_WithInvalidId_ShouldReturnNotFound()
        {
            // Arrange
            Guid personId = Guid.NewGuid();

            _personServicesMock
                .Setup(x => x.GetPersonByPersonId(personId))
                .ReturnsAsync((PersonRespones?)null);

            // Act
            IActionResult result = await _personController.Edit(personId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        #endregion

        #region Edit (POST)

        [Fact]
        public async Task Edit_Post_ValidModel_ShouldRedirectToIndex()
        {
            // Arrange
            PersonUpdateRequest model = _fixture.Create<PersonUpdateRequest>();
            PersonRespones updatedPerson = _fixture.Create<PersonRespones>();
            List<CountryResponse> countries = _fixture.Create<List<CountryResponse>>();

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            _personServicesMock
                .Setup(x => x.UpdatePerson(model))
                .ReturnsAsync(updatedPerson);

            // Act
            IActionResult result = await _personController.Edit(model);

            // Assert
            RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Person", redirectResult.ControllerName);

            _personServicesMock.Verify(x => x.UpdatePerson(model), Times.Once);
        }

        [Fact]
        public async Task Edit_Post_ValidModel_UpdateFails_ShouldReturnNotFound()
        {
            // Arrange
            PersonUpdateRequest model = _fixture.Create<PersonUpdateRequest>();
            List<CountryResponse> countries = _fixture.Create<List<CountryResponse>>();

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            _personServicesMock
                .Setup(x => x.UpdatePerson(model))
                .ReturnsAsync((PersonRespones?)null);

            // Act
            IActionResult result = await _personController.Edit(model);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Edit_Post_InvalidModel_ShouldReturnViewWithModel()
        {
            // Arrange
            PersonUpdateRequest model = _fixture.Create<PersonUpdateRequest>();
            List<CountryResponse> countries = _fixture.Create<List<CountryResponse>>();

            _personController.ModelState.AddModelError("Name", "Name is required");

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            // Act
            IActionResult result = await _personController.Edit(model);

            // Assert
            ViewResult viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal(model, viewResult.Model);

            var modelErrors = _personController.ViewBag.ModelErrors;
            Assert.NotNull(modelErrors);

            _personServicesMock.Verify(x => x.UpdatePerson(It.IsAny<PersonUpdateRequest>()), Times.Never);
        }

        #endregion

        #region Delete (GET)

        [Fact]
        public async Task Delete_Get_WithValidId_ShouldReturnViewWithPerson()
        {
            // Arrange
            Guid personId = Guid.NewGuid();
            PersonRespones person = _fixture.Create<PersonRespones>();

            _personServicesMock
                .Setup(x => x.GetPersonByPersonId(personId))
                .ReturnsAsync(person);

            // Act
            IActionResult result = await _personController.Delete(personId);

            // Assert
            ViewResult viewResult = Assert.IsType<ViewResult>(result);
            PersonRespones? model = Assert.IsType<PersonRespones>(viewResult.Model);
            Assert.Equal(person, model);
        }

        [Fact]
        public async Task Delete_Get_WithInvalidId_ShouldRedirectToIndex()
        {
            // Arrange
            Guid personId = Guid.NewGuid();

            _personServicesMock
                .Setup(x => x.GetPersonByPersonId(personId))
                .ReturnsAsync((PersonRespones?)null);

            // Act
            IActionResult result = await _personController.Delete(personId);

            // Assert
            RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Person", redirectResult.ControllerName);
        }

        #endregion

        #region Delete (POST)

        [Fact]
        public async Task Delete_Post_ValidModel_ShouldDeleteAndRedirectToIndex()
        {
            // Arrange
            PersonRespones model = _fixture.Create<PersonRespones>();

            _personServicesMock
                .Setup(x => x.DeletePersonByPersonId(model.PersonId))
                .ReturnsAsync(true);

            // Act
            IActionResult result = await _personController.Delete(model);

            // Assert
            RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Person", redirectResult.ControllerName);

            _personServicesMock.Verify(x => x.DeletePersonByPersonId(model.PersonId), Times.Once);
        }

        [Fact]
        public async Task Delete_Post_DeleteFails_ShouldStillRedirectToIndex()
        {
            // Arrange
            PersonRespones model = _fixture.Create<PersonRespones>();

            _personServicesMock
                .Setup(x => x.DeletePersonByPersonId(model.PersonId))
                .ReturnsAsync(false);

            // Act
            IActionResult result = await _personController.Delete(model);

            // Assert
            RedirectToActionResult redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Person", redirectResult.ControllerName);

            _personServicesMock.Verify(x => x.DeletePersonByPersonId(model.PersonId), Times.Once);
        }

        #endregion

        #region Index

        [Fact]
        public async Task Index_WithValidParameters_ShouldReturnViewWithPersons()
        {
            // Arrange
            string searchBy = "Name";
            string searchString = "John";
            string sortedBy = "Name";
            sortedListOp sortOption = sortedListOp.Ascending;

            List<PersonRespones> persons = _fixture.Create<List<PersonRespones>>();
            List<PersonRespones> sortedPersons = _fixture.Create<List<PersonRespones>>();

            _personServicesMock
                .Setup(x => x.SearchPersonsBy(searchString, searchBy))
                .ReturnsAsync(persons);

            _personServicesMock
                .Setup(x => x.getPersonsSorted(persons, sortedBy, sortOption))
                .ReturnsAsync(sortedPersons);

            // Act
            IActionResult result = await _personController.Index(searchBy, searchString, sortedBy, sortOption);

            // Assert
            ViewResult viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<PersonRespones>>(viewResult.Model);
            Assert.Equal(sortedPersons, model);

            // Verify ViewBag values
            Assert.Equal(searchBy, _personController.ViewBag.CurrentSearchBy);
            Assert.Equal(searchString, _personController.ViewBag.CurrentSearchString);
            Assert.Equal(sortedBy, _personController.ViewBag.CurrentSortedBy);
            Assert.Equal(sortOption.ToString(), _personController.ViewBag.CurrentSortOption);

            // Verify ViewBag contains search fields
            var searchFields = _personController.ViewBag.SearchFields;
            Assert.NotNull(searchFields);

            _personServicesMock.Verify(x => x.SearchPersonsBy(searchString, searchBy), Times.Once);
            _personServicesMock.Verify(x => x.getPersonsSorted(persons, sortedBy, sortOption), Times.Once);
        }

        [Fact]
        public async Task Index_WithNullParameters_ShouldHandleGracefully()
        {
            // Arrange
            string? searchBy = null;
            string? searchString = null;
            string? sortedBy = null;
            sortedListOp sortOption = sortedListOp.Ascending;

            List<PersonRespones> persons = _fixture.Create<List<PersonRespones>>();
            List<PersonRespones> sortedPersons = _fixture.Create<List<PersonRespones>>();

            _personServicesMock
                .Setup(x => x.SearchPersonsBy(searchString, searchBy))
                .ReturnsAsync(persons);

            _personServicesMock
                .Setup(x => x.getPersonsSorted(persons, sortedBy, sortOption))
                .ReturnsAsync(sortedPersons);

            // Act
            IActionResult result = await _personController.Index(searchBy, searchString, sortedBy, sortOption);

            // Assert
            ViewResult viewResult = Assert.IsType<ViewResult>(result);
            Assert.NotNull(viewResult.Model);
        }

        [Fact]
        public async Task Index_WhenExceptionOccurs_ShouldReturnErrorView()
        {
            // Arrange
            string searchBy = "Name";
            string searchString = "John";
            string sortedBy = "Name";
            sortedListOp sortOption = sortedListOp.Ascending;

            _personServicesMock
                .Setup(x => x.SearchPersonsBy(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            IActionResult result = await _personController.Index(searchBy, searchString, sortedBy, sortOption);

            // Assert
            ViewResult viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Error", viewResult.ViewName);
        }

        [Fact]
        public async Task Index_ShouldPopulateSearchFieldsDictionary()
        {
            // Arrange
            List<PersonRespones> persons = _fixture.Create<List<PersonRespones>>();

            _personServicesMock
                .Setup(x => x.SearchPersonsBy(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(persons);

            _personServicesMock
                .Setup(x => x.getPersonsSorted(It.IsAny<List<PersonRespones>>(), It.IsAny<string>(), It.IsAny<sortedListOp>()))
                .ReturnsAsync(persons);

            // Act
            await _personController.Index(null, null, null, sortedListOp.Ascending);

            // Assert
            var searchFields = _personController.ViewBag.SearchFields as Dictionary<string, string>;
            Assert.NotNull(searchFields);
            Assert.Contains(nameof(Person.email), searchFields.Keys);
            Assert.Contains(nameof(Person.Gender), searchFields.Keys);
            Assert.Contains(nameof(Person.NewsLetter), searchFields.Keys);
            Assert.Contains(nameof(Person.DateOfBirth), searchFields.Keys);
            Assert.Contains(nameof(Person.phone), searchFields.Keys);
            Assert.Contains(nameof(Person.CountryId), searchFields.Keys);
            Assert.Contains(nameof(Person.Name), searchFields.Keys);
        }

        #endregion

        #region Route Attribute Tests

        [Fact]
        public void Index_HasCorrectRouteAttributes()
        {
            // Act
            var routeAttribute = typeof(PersonController)
                .GetMethod(nameof(PersonController.Index))
                ?.GetCustomAttributes(typeof(RouteAttribute), false)
                .FirstOrDefault() as RouteAttribute;

            // Assert
            Assert.NotNull(routeAttribute);
            Assert.Equal("Person/index", routeAttribute.Template);
        }

        #endregion
    }
}