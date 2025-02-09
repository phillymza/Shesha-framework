﻿using Abp.Dependency;
using NHibernate;
using NHibernate.Persister.Entity;
using NHibernate.Type;
using Shesha.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shesha.Configuration.MappingMetadata;
using System.Reflection;
using NHibernate.Mapping;
using NUglify;

namespace Shesha.MappingMetadata
{
    public class MappingMetadataProvider: IMappingMetadataProvider, ITransientDependency
    {

        private readonly ISessionFactory _sessionFactory;

        public MappingMetadataProvider(ISessionFactory sessionFactory)
        {
            _sessionFactory = sessionFactory;
        }

        public EntityMappingMetadata GetEntityMappingMetadata(Type entityType)
        {
            var persister = _sessionFactory.GetClassMetadata(entityType) as SingleTableEntityPersister;

            var mappingMetadata = new EntityMappingMetadata()
            {
                TableName = persister?.TableName,
                DiscriminatorValue = persister?.DiscriminatorSQLValue,
                IsMultiTable = persister?.IsMultiTable ?? false,
            };
            mappingMetadata.SubclassTableName = mappingMetadata.IsMultiTable
                ? persister?.GetSubclassTableName(1)
                : null;

            return mappingMetadata;
        }

        public PropertyMappingMetadata GetPropertyMappingMetadata(Type entityType, string propertyName)
        {
            var persister = _sessionFactory.GetClassMetadata(entityType) as SingleTableEntityPersister;

            var propertyMetadata = new PropertyMappingMetadata
            {
                ColumnNames = persister?.GetPropertyColumnNames(propertyName),
                TableName = persister?.GetPropertyTableName(propertyName),
            };
            return propertyMetadata;
        }

        public async Task UpdateClassNames(Type entityType, List<PropertyInfo> properties, string oldValue, string newValue, bool replace)
        {
            var session = _sessionFactory.GetCurrentSession();

            var propsMetadata = properties.Select(x => GetPropertyMappingMetadata(entityType, x.Name)).ToList();

            if (replace)
            {
                var tables = propsMetadata.GroupBy(x => x.TableName).ToList();

                foreach (var table in tables)
                {
                    var sqlBuilder = new StringBuilder();

                    sqlBuilder.Append($"update {table.Key} set ");

                    var props = table.ToList();

                    sqlBuilder.Append(
                        string.Join(", ",
                        props.Select(x => $"{x.ColumnNames[0]} = replace({x.ColumnNames[0]}, '\"{oldValue}\"', '\"{newValue}\"')")
                        ));

                    var sql = sqlBuilder.ToString();

                    var q = session.CreateSQLQuery(sql);
                    var i = await q.ExecuteUpdateAsync();
                }
            }
            else
            {
                foreach(var property in propsMetadata)
                {
                    var propName = property.ColumnNames.FirstOrDefault(x => x.ToLower().EndsWith("classname"));
                    var sql = $"update {property.TableName} set {propName} = '{newValue}' where {propName} = '{oldValue}'";
                    var q = session.CreateSQLQuery(sql);
                    var i = await q.ExecuteUpdateAsync();
                }
            }
        }
    }
}
