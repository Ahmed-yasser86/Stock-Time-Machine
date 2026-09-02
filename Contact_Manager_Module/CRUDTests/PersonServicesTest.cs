using AutoFixture;
using Castle.Core.Logging;
using Entities;
using EntityFrameworkCoreMock;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RepositryContracts;
using ServiceContracts;
using ServiceContracts.DTOs;
using ServiceContracts.DTOs.Enums;
using Servicess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CRUDTests
{
    public class PersonServicesTest
    {
        private readonly IPersonServices _personServices;
        private readonly ICountryServices _countryServices;
        private readonly IFixture _fixture;
        private readonly Mock<PersonRepositryContract> _personRepositryContractMoq;
        private readonly PersonRepositryContract _personRepositryContract;

        public PersonServicesTest()
        {
            _fixture = new Fixture();

            List<Person> persons = new List<Person>();
            List<Country> countries = new List<Country>();
            DbContextMock<AppDBContext> dbContextMock = new DbContextMock<AppDBContext>(new DbContextOptionsBuilder<AppDBContext>().Options);

            _personRepositryContractMoq = new Mock<PersonRepositryContract>();
            _personRepositryContract = _personRepositryContractMoq.Object;

            dbContextMock.CreateDbSetMock(temp => temp.Persons, persons);
            dbContextMock.CreateDbSetMock(temp => temp.Countries, countries);

            // FIXED: Create logger mock instead of actual logger
            var loggerMock = new Mock<ILogger<PersonServices>>();
            ILogger<PersonServices> logger = loggerMock.Object;

            _personServices = new Servicess.PersonServices(_personRepositryContract, logger);
            //    _countryServices = new Servicess.CountryServices(null);
        }

        #region AddPerson Tests

        [Fact]
        public async Task AddPerson_null()
        {
            // Arrange
            PersonAddRequest? personAddRequest = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _personServices.AddPerson(personAddRequest));
        }

        [Fact]
        public async Task AddPerson_nullPersonName()
        {
            // Arrange
            PersonAddRequest? personAddRequest = _fixture.Build<PersonAddRequest>()
                .With(p => p.Name, (string?)null)
                .With(p => p.email, "test@example.com")
                .With(p => p.phone, "123456789")
                .With(p => p.Gender, GenderOptions.Male)
                .Create();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => await _personServices.AddPerson(personAddRequest));
        }

        [Fact]
        public async Task AddPerson_ProperPersonDetails_PersonAddSuccessfully()
        {
            // Arrange
            PersonAddRequest? personAddRequest = _fixture.Build<PersonAddRequest>()
                .With(p => p.email, "test@example.com")
                .With(p => p.phone, "123456789")
                .With(p => p.Gender, GenderOptions.Male)
                .Create();

            Person person = personAddRequest.ToPerson();
            PersonRespones personResponesobj = person.ConvertToPersonRespons();

            // Act
            var personResponse_expecteed = await _personServices.AddPerson(personAddRequest);

            _personRepositryContractMoq.Setup(repo => repo.AddPerson(It.IsAny<Person>())).ReturnsAsync(personAddRequest.ToPerson());

            personResponse_expecteed.PersonId.Should().NotBe(Guid.Empty);
            personResponesobj.PersonId = personResponse_expecteed.PersonId;
            personResponesobj.Should().Be(personResponse_expecteed);
        }

        #endregion

        #region GetPersonByPersonId Tests

        [Fact]
        public async Task GetPersonByPersonId_null()
        {
            // Arrange
            Guid? personId = null;

            // Act
            var k = await _personServices.GetPersonByPersonId(personId);

            // Assert
            Assert.Null(k);
        }

        [Fact]
        public async Task GetPersonByPersonID_WithPersonID_ToBeSuccessful()
        {
            // Arrange
            Person person = _fixture.Build<Person>()
                .With(temp => temp.email, "email@sample.com")
                .With(temp => temp.Country, null as Country)
                .Create();

            // Retained the original misspelled class and method names here:
            PersonRespones person_response_expected = person.ConvertToPersonRespons();

            _personRepositryContractMoq.Setup(temp => temp.GetPersonById(It.IsAny<Guid>()))
                .ReturnsAsync(person);

            // Act
            // Retained the misspelled return type here:
            PersonRespones? person_response_from_get = await _personServices.GetPersonByPersonId(person.PersonId);

            // Assert
            person_response_from_get.Should().Be(person_response_expected);
        }

        #endregion

        #region GetAllPersons Tests

        [Fact]
        public async Task GetAllPersons_Test()
        {
            // Arrange
            List<Person> persons = new List<Person>()
{
    _fixture.Build<Person>()
        .With(p => p.email, "p1@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create(),

    _fixture.Build<Person>()
        .With(p => p.email, "p2@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create(),

    _fixture.Build<Person>()
        .With(p => p.email, "p3@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create()
};

            _personRepositryContractMoq.Setup(repo => repo.GetAllPersons()).ReturnsAsync(persons);

            List<PersonRespones> expected_persons = persons.Select(p => p.ConvertToPersonRespons()).ToList();

            // Act
            List<PersonRespones> actual_persons = await _personServices.GetAllPersons();

            // Assert
            actual_persons.Should().BeEquivalentTo(expected_persons);
        }

        #endregion

        #region SearchBy Tests

        [Fact]
        public async Task GetPersonsByName_EmptySearchText_ToBeSuccessful()
        {
            List<Person> persons = new List<Person>()
{
    _fixture.Build<Person>()
        .With(p => p.email, "p1@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create(),

    _fixture.Build<Person>()
        .With(p => p.email, "p2@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create(),

    _fixture.Build<Person>()
        .With(p => p.email, "p3@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create()
};

            _personRepositryContractMoq.Setup(repo => repo.GetAllPersons()).ReturnsAsync(persons);

            // Act
            List<PersonRespones> actualList = await _personServices.SearchPersonsBy(nameof(Person.Name), string.Empty);

            // Assert
            actualList.Should().BeEquivalentTo(persons.Select(p => p.ConvertToPersonRespons()).ToList());
        }

        [Fact]
        public async Task GetFilteredPersons_EmptySearchText_ToBeSuccess()
        {
            List<Person> persons = new List<Person>()
{
    _fixture.Build<Person>()
        .With(p => p.email, "p1@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create(),

    _fixture.Build<Person>()
        .With(p => p.email, "p2@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create(),

    _fixture.Build<Person>()
        .With(p => p.email, "p3@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create()
};

            _personRepositryContractMoq.Setup(repo => repo.GetAllPersons()).ReturnsAsync(persons);

            // Act
            List<PersonRespones> actualList = await _personServices.SearchPersonsBy(nameof(Person.Name), "sa");

            // Assert
            actualList.Should().BeEquivalentTo(persons.Select(p => p.ConvertToPersonRespons()).ToList());
        }

        #endregion

        #region GetPersonSorted Tests

        [Fact]
        public async Task GetPersonsSorted_DESC_Test()
        {
            // Arrange
            List<Person> persons = new List<Person>()
{
    _fixture.Build<Person>()
        .With(p => p.email, "p1@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create(),

    _fixture.Build<Person>()
        .With(p => p.email, "p2@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create(),

    _fixture.Build<Person>()
        .With(p => p.email, "p3@example.com")
        .With(p => p.phone, "123456789")
        .With(p => p.Country, null as Country)
        .Create()
};

            _personRepositryContractMoq.Setup(repo => repo.GetAllPersons()).ReturnsAsync(persons);

            var personToSort = persons.Select(p => p.ConvertToPersonRespons()).ToList();

            // Act
            List<PersonRespones> actualList = await _personServices.getPersonsSorted(personToSort, nameof(Person.Name), sortedListOp.Descending);

            // Assert
            actualList.Should().BeInDescendingOrder(p => p.Name);
        }

        #endregion

        #region UpdatePerson Tests

        [Fact]
        public async Task UpdatePerson_ProperDetails_IdIsNull_Test()
        {
            // Arrange
            PersonUpdateRequest personUpdateRequest = _fixture.Build<PersonUpdateRequest>()
                .With(p => p.PersonId, (Guid?)null)
                .With(p => p.Name, (string?)null)
                .With(p => p.email, "test@example.com")
                .With(p => p.phone, "123456789")
                .Create();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => await _personServices.UpdatePerson(personUpdateRequest));
        }

        [Fact]
        public async Task UpdatePerson_Null_Test()
        {
            // Arrange
            PersonUpdateRequest? personUpdateRequest = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _personServices.UpdatePerson(personUpdateRequest));
        }

        [Fact]
        public async Task UpdatePerson_ProperDetails_NameIsNull_Test()
        {
            // Arrange
            Person person = _fixture.Build<Person>()
                .With(p => p.email, "test@example.com")
                .With(p => p.phone, "123456789")
                .Without(p => p.Country)
                .With(p => p.Gender, "Male")
                .Create();

            _personRepositryContractMoq.Setup(repo => repo.UpdatePerson(It.IsAny<Person>())).ReturnsAsync(person);

            PersonUpdateRequest ToUpdate = person.ConvertToPersonRespons().ToPersonUpdateRequest();
            ToUpdate.Name = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => await _personServices.UpdatePerson(ToUpdate));
        }

        [Fact]
        public async Task UpdatePerson_ProperDetails_Test()
        {
            // Arrange
            Person person = _fixture.Build<Person>()
                .With(p => p.email, "test@example.com")
                .With(p => p.phone, "123456789")
                .Without(p => p.Country)
                .With(p => p.Gender, "Male")
                .Create();

            _personRepositryContractMoq.Setup(repo => repo.UpdatePerson(It.IsAny<Person>())).ReturnsAsync(person);

            PersonUpdateRequest ToUpdate = person.ConvertToPersonRespons().ToPersonUpdateRequest();
            ToUpdate.Name = "karim";
            ToUpdate.DateOfBirth = new DateTime(1990, 1, 1);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => await _personServices.UpdatePerson(ToUpdate));
        }

        #endregion

        #region DeletePersonByPersonId Tests

        [Fact]
        public async Task DeletePersonByPersonId_Null_Test()
        {
            // Arrange
            Guid? personId = null;

            // Act & Assert
            Assert.False(await _personServices.DeletePersonByPersonId(personId));
        }

        [Fact]
        public async Task DeletePersonByPersonId_ValidTest_Test()
        {
            Person person = _fixture.Build<Person>()
                 .With(p => p.PersonId, Guid.NewGuid())
                 .With(p => p.email, "test@example.com")
                 .With(p => p.phone, "123456789")
                 .Without(p => p.Country)
                 .Create();

            Assert.NotNull(person);

            _personRepositryContractMoq
                .Setup(repo => repo.DeletePerson(It.IsAny<Guid?>()))
                .ReturnsAsync(true);

            _personRepositryContractMoq.Setup(repo => repo.GetPersonById(It.IsAny<Guid>()))
               .ReturnsAsync(person);

            // Act
            bool isTrue = await _personServices.DeletePersonByPersonId(person.PersonId);

            // Assert
            Assert.True(isTrue);
        }

        [Fact]
        public async Task DeletePersonByPersonId_InValidTest_Test()
        {
            // Arrange
            Person person = _fixture.Build<Person>()
                 .With(p => p.PersonId, Guid.NewGuid())
                 .With(p => p.email, "test@example.com")
                 .With(p => p.phone, "123456789")
                 .Without(p => p.Country)
                 .Create();

            Assert.NotNull(person);

            _personRepositryContractMoq
                .Setup(repo => repo.DeletePerson(It.IsAny<Guid?>()))
                .ReturnsAsync(false);

            _personRepositryContractMoq.Setup(repo => repo.GetPersonById(It.IsAny<Guid>()))
                .ReturnsAsync(person);

            // Act
            bool isfalse = await _personServices.DeletePersonByPersonId(Guid.NewGuid());

            // Assert
            Assert.True(!isfalse);
        }

        #endregion
    }
}