using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoFixture;
using CRUDTests.CustomFactory;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using ServiceContracts;
using ServiceContracts.DTOs;
using ServiceContracts.DTOs.Enums;
using Xunit;

namespace CRUDTests.IntegrationTest
{
    public class PersonsIntegrationTest : IClassFixture<CustomWebApplicationFactory>, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly CustomWebApplicationFactory _factory;
        private readonly Mock<IPersonServices> _personServicesMock;
        private readonly Mock<ICountryServices> _countryServicesMock;
        private readonly Fixture _fixture;

        public PersonsIntegrationTest(CustomWebApplicationFactory factory)
        {
            _factory = factory;
            _httpClient = factory.CreateClient();
            _personServicesMock = factory.PersonServicesMock;
            _countryServicesMock = factory.CountryServicesMock;

            _fixture = new Fixture();
            _fixture.Behaviors.OfType<ThrowingRecursionBehavior>()
                .ToList()
                .ForEach(b => _fixture.Behaviors.Remove(b));
            _fixture.Behaviors.Add(new OmitOnRecursionBehavior());
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #region Index Tests

        [Fact]
        public async Task Index_Get_ShouldReturnViewWithPersons()
        {
            // Arrange
            var expectedPersons = _fixture.Create<List<PersonRespones>>();

            _personServicesMock
                .Setup(x => x.SearchPersonsBy(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(expectedPersons);

            _personServicesMock
                .Setup(x => x.getPersonsSorted(It.IsAny<List<PersonRespones>>(), It.IsAny<string>(), It.IsAny<sortedListOp>()))
                .ReturnsAsync(expectedPersons);

            // Act
            HttpResponseMessage response = await _httpClient.GetAsync("/Person/index");
            string responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            responseBody.Should().Contain("People Directory");
        }

        [Fact]
        public async Task Index_WithSearchParameters_ShouldReturnFilteredResults()
        {
            // Arrange
            var persons = _fixture.Create<List<PersonRespones>>();

            _personServicesMock
                .Setup(x => x.SearchPersonsBy("John", "Name"))
                .ReturnsAsync(persons);

            _personServicesMock
                .Setup(x => x.getPersonsSorted(persons, "Name", sortedListOp.Ascending))
                .ReturnsAsync(persons);

            // Act
            HttpResponseMessage response = await _httpClient.GetAsync("/Person/index?SearchBy=Name&SearchString=John&SortedBy=Name&SortOption=Ascending");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            _personServicesMock.Verify(x => x.SearchPersonsBy("John", "Name"), Times.Once);
        }

        #endregion

        #region Create Tests (GET)

        [Fact]
        public async Task Create_Get_ShouldReturnCreateForm()
        {
            // Arrange
            var countries = _fixture.Create<List<CountryResponse>>();

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            // Act
            HttpResponseMessage response = await _httpClient.GetAsync("/Person/Create");
            string responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            responseBody.Should().Contain("Create Person");
        }

        #endregion

        #region Create Tests (POST)

        [Fact]
        public async Task Create_Post_WithMissingRequiredFields_ShouldReturnViewWithErrors()
        {
            // Arrange
            var countries = _fixture.Create<List<CountryResponse>>();

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            var formData = new Dictionary<string, string>
            {
                { "Name", "" },
                { "email", "" },
                { "phone", "1234567890" }
            };

            var content = new FormUrlEncodedContent(formData);

            // Act
            HttpResponseMessage response = await _httpClient.PostAsync("/Person/Create", content);
            string responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            responseBody.Should().Contain("Create Person");
            _personServicesMock.Verify(x => x.AddPerson(It.IsAny<PersonAddRequest>()), Times.Never);
        }

        [Fact]
        public async Task Create_Post_WithInvalidEmail_ShouldReturnViewWithErrors()
        {
            // Arrange
            var countries = _fixture.Create<List<CountryResponse>>();

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            var formData = new Dictionary<string, string>
            {
                { "Name", "Test User" },
                { "email", "invalid-email" },
                { "phone", "1234567890" },
                { "Gender", "Male" },
                { "CountryId", Guid.NewGuid().ToString() }
            };

            var content = new FormUrlEncodedContent(formData);

            // Act
            HttpResponseMessage response = await _httpClient.PostAsync("/Person/Create", content);
            string responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            responseBody.Should().Contain("Create Person");
            _personServicesMock.Verify(x => x.AddPerson(It.IsAny<PersonAddRequest>()), Times.Never);
        }

        #endregion

        #region Edit Tests (GET)

        [Fact]
        public async Task Edit_Get_WithInvalidId_ShouldReturnNotFound()
        {
            // Arrange
            var invalidId = Guid.NewGuid();

            _personServicesMock
                .Setup(x => x.GetPersonByPersonId(invalidId))
                .ReturnsAsync((PersonRespones?)null);

            // Act
            HttpResponseMessage response = await _httpClient.GetAsync($"/Person/Edit/{invalidId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        #endregion

        #region Edit Tests (POST)

        [Fact]
        public async Task Edit_Post_WithInvalidModel_ShouldReturnViewWithErrors()
        {
            // Arrange
            var personId = Guid.NewGuid();
            var countries = _fixture.Create<List<CountryResponse>>();

            _countryServicesMock
                .Setup(x => x.Countries())
                .ReturnsAsync(countries);

            var formData = new Dictionary<string, string>
            {
                { "PersonId", personId.ToString() },
                { "Name", "" },
                { "email", "test@test.com" }
            };

            var content = new FormUrlEncodedContent(formData);

            // Act
            HttpResponseMessage response = await _httpClient.PostAsync($"/Person/Edit/{personId}", content);
            string responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            responseBody.Should().Contain("Edit Person");
            _personServicesMock.Verify(x => x.UpdatePerson(It.IsAny<PersonUpdateRequest>()), Times.Never);
        }

        #endregion

        #region Delete Tests (GET)

        [Fact]
        public async Task Delete_Get_WithValidId_ShouldReturnDeleteConfirmation()
        {
            // Arrange
            var personId = Guid.NewGuid();
            var person = _fixture.Build<PersonRespones>()
                .With(x => x.PersonId, personId)
                .With(x => x.Name, "User To Delete")
                .With(x => x.email, "delete@test.com")
                .Create();

            _personServicesMock
                .Setup(x => x.GetPersonByPersonId(personId))
                .ReturnsAsync(person);

            // Act
            HttpResponseMessage response = await _httpClient.GetAsync($"/Person/Delete/{personId}");
            string responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            responseBody.Should().Contain("Delete Person");
            responseBody.Should().Contain("User To Delete");
        }

        #endregion

        #region Root Route Tests

        [Fact]
        public async Task RootRoute_ShouldReturnIndexView()
        {
            // Arrange
            var persons = _fixture.Create<List<PersonRespones>>();

            _personServicesMock
                .Setup(x => x.SearchPersonsBy(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(persons);

            _personServicesMock
                .Setup(x => x.getPersonsSorted(It.IsAny<List<PersonRespones>>(), It.IsAny<string>(), It.IsAny<sortedListOp>()))
                .ReturnsAsync(persons);

            // Act
            HttpResponseMessage response = await _httpClient.GetAsync("/");
            string responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            responseBody.Should().Contain("People Directory");
        }

        #endregion
    }
}