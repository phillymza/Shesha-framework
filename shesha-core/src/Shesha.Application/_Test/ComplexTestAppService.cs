﻿using Abp.Dependency;
using Abp.Domain.Repositories;
using Castle.DynamicProxy;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shesha.AutoMapper.Dto;
using Shesha.Domain;
using Shesha.DynamicEntities;
using Shesha.DynamicEntities.Dtos;
using Shesha.EntityReferences;
using Shesha.Extensions;
using Shesha.JsonEntities;
using Shesha.NHibernate.Utilites;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace Shesha.Test
{
    public class ComplexTestHardAppService : DynamicCrudAppService<ComplexTest, DynamicDto<ComplexTest, Guid>, Guid>, ITransientDependency
    {

        private readonly IRepository<Person, Guid> _personRepository;

        public ComplexTestHardAppService(
            IRepository<ComplexTest, Guid> repository,
            IRepository<Person, Guid> personRepository
            ) : base(repository)
        {
            _personRepository = personRepository;
        }

        [HttpGet]
        public async Task<List<object>> CheckGenericEntity()
        {
            var entities = Repository.GetAll().Where(x => x.AnyEntity != null).ToList();
            var entity = entities.FirstOrDefault(x => x.AnyEntity._className == typeof(Person).FullName && x.AnyEntity.Id == "b3b60f2e-5b88-4f44-b8eb-d3987a8483d9");

            var person = (Person)entity.AnyEntity;

            var dbEntity = Repository.GetAll().FirstOrDefault(x => x.AnyEntity._className == typeof(Person).FullName && x.AnyEntity.Id == "b3b60f2e-5b88-4f44-b8eb-d3987a8483d9");

            var dbPerson = (Person)dbEntity.AnyEntity;

            return new List<object> { 
                new { id = person.Id, fullName = person.FullName }, 
                new { id = dbPerson.Id, fullName = dbPerson.FullName }
            };
        }


        [HttpGet]
        public async Task<JObject> CheckJObject()
        {
            var obj = new JObject();
            obj.Add(new JProperty("name", "Shurik"));
            obj.Add(new JProperty("date", DateTime.Now));
            obj.Add(new JProperty("innerObject", new JObject()
            {
                new JProperty("firstName", "Alex"),
                new JProperty("lastName", "Stephens"),
                new JProperty("age", 43)
            }));

            return obj;
        }

        [HttpGet]
        public async Task CheckData()
        {
            var obj = Repository.GetAll().ToList();

        }

        [HttpGet]
        public async Task CreateDate()
        {

            var o = new ComplexTest()
            {
                Name = "Any Person test",
                Description = "Any Person Description",
                //JsonList = new List<JsonEntity>()
                //{
                //    new JsonEntity(new JsonAddress() { Town = "Listed Town", Street = "Listed street" }),
                //    new JsonEntity(new JsonPerson() { FirstName = "11", LastName = "qq" }),
                //    new JsonEntity(new JsonPerson() { FirstName = "22", LastName = "ww" }),
                //    new JsonEntity(new JsonPerson() { FirstName = "33", LastName = "ee" }),
                //},
                AnyJson = new JsonPerson()
                {
                    FirstName = "Any first name",
                    LastName = "Any last name",
                    //Persons = new List<JsonPerson>()
                    //{
                    //    new JsonPerson() { FirstName = "++11", LastName = "++qq" },
                    //    new JsonPerson() { FirstName = "++22", LastName = "++ww" },
                    //    new JsonPerson() { FirstName = "++33", LastName = "++ee" },
                    //},
                },
                JsonPerson = new JsonPerson()
                {
                    FirstName = "11111",
                    LastName = "22222",
                    //Person = _personRepository.Get(Guid.Parse("B3B60F2E-5B88-4F44-B8EB-D3987A8483D9"))
                },
                AnyEntity = _personRepository.Get(Guid.Parse("B3B60F2E-5B88-4F44-B8EB-D3987A8483D9"))
            };
            await Repository.InsertOrUpdateAsync(o);

            #region
            //o = new ComplexTest()
            //{
            //    Name = "Any Address test",
            //    Description = "Any Address Description",
            //    Any = new JsonEntity(new JsonAddress()
            //    {
            //        Town = "London",
            //        Street = "Backer street",
            //        PostalCode = 12345678
            //    })
            //};
            //await Repository.InsertOrUpdateAsync(o);

            //o = new ComplexTest()
            //{
            //    Name = "Any Organisation test",
            //    Description = "Any Organisation Description",
            //    Any = new JsonEntity(new JsonOrganisation()
            //    {
            //        Name = "Json Organisaion",
            //        Address = new JsonAddress()
            //        {
            //            Town = "New York",
            //            Street = "123 avenue",
            //            PostalCode = 654321
            //        }
            //    })
            //};
            //await Repository.InsertOrUpdateAsync(o);
            #endregion
        }

        [HttpGet]
        public async Task CreateTable()
        {
            var connStr = NHibernateUtilities.ConnectionString;

            using (var con = new SqlConnection(connStr))
            {
                con.Open();
                var backupCommand = new SqlCommand(@"
                    CREATE TABLE [dbo].[Test_ComplexTest](
	                    [Id] [uniqueidentifier] NOT NULL,
	                    [Name] [nvarchar](100) NULL,
	                    [Description] [nvarchar](500) NULL,
	                    [JsonPerson] [nvarchar](max) NULL,
	                    [JsonList] [nvarchar](max) NULL,
	                    [AnyJson] [nvarchar](max) NULL,
	                    [Json] [nvarchar](max) NULL,
	                    [PersonId] [uniqueidentifier] NULL,
	                    [AnyEntityId] [nvarchar](100) NULL,
	                    [AnyEntityClassName] [nvarchar](1000) NULL,
	                    [AnyEntityDisplayName] [nvarchar](1000) NULL,
                    CONSTRAINT [PK_Test_CopmlexTest] PRIMARY KEY CLUSTERED ([Id] ASC)
                    WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
                    ) ON [PRIMARY]
                    ", con);
                await backupCommand.ExecuteNonQueryAsync();
            }
        }

        [HttpGet]
        public async Task DeleteTable()
        {
            var connStr = NHibernateUtilities.ConnectionString;

            using (var con = new SqlConnection(connStr))
            {
                con.Open();
                var backupCommand = new SqlCommand("DROP TABLE [dbo].[Test_ComplexTest]", con);
                await backupCommand.ExecuteNonQueryAsync();
            }
        }
    }
}