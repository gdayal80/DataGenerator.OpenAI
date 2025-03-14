namespace DataGenerator.OpenAI.Data.Generators
{
    using System.Reflection;
    using Microsoft.EntityFrameworkCore;
    using DataGenerator.OpenAI.Interfaces;
    using DataGenerator.OpenAI.Types;
    using DataGenerator.OpenAI.Repositories.Generic;
    using Microsoft.EntityFrameworkCore.Metadata;
    using DataGenerator.OpenAI.Data.Analysers;
    using System.Text.Json;
    using DataGenerator.OpenAI.Mock.Data.Generators;
    using global::OpenAI.Chat;
    
    public class EntityFrameworkDataGenerator<T> where T : DbContext
    {
        private T _context;
        private EntityFrameworkAnalyser<T> _analyser;
        private MockDataGenerator _generator;
        private ITraceWriter _trace;
        private IEnumerable<IEntityType> _entityTypes;
        private List<Entity> _generatedEntities;
        private Random _random;

        public EntityFrameworkDataGenerator(T context, MockDataGenerator generator, ITraceWriter trace)
        {
            _context = context;
            _analyser = new EntityFrameworkAnalyser<T>(trace);
            _generator = generator;
            _trace = trace;
            _entityTypes = _analyser.GetEntityTypesFromModel(context);
            _generatedEntities = new List<Entity>();
            _random = new Random();
        }

        public async Task GenerateAndInsertDataWithRandomForeignKeys<K>(string locale, int noOfRows = 2, int openAiBatchSize = 5, string inDataValue = "", int retryCount = 3, params string[] coomonColumnsToIgnore) where K : class
        {
            int batchArrSize = noOfRows / openAiBatchSize;
            int remainder = noOfRows % openAiBatchSize;
            List<int> batchArr = new List<int>(remainder > 0 ? batchArrSize + 1 : batchArrSize);
            var entity = _analyser.AnalyseEntity<K>(_entityTypes);
            ChatCompletionOptions? completionOptions;
            
            for (int i = 0; i < batchArrSize; i++)
            {
                batchArr.Add(openAiBatchSize);
            }

            if (remainder > 0)
            {
                batchArr.Add(remainder);
            }

            for (int i = 0; i < batchArr.Count(); i++)
            {
                var message = _generator.GenerateMessage(entity!, locale, out completionOptions, batchArr[i], inDataValue, coomonColumnsToIgnore);
                int count = 1;

                while (retryCount > 0)
                {
                    var data = await _generator.GenerateMockData(message, completionOptions);

                    if (!string.IsNullOrEmpty(data))
                    {
                        try
                        {
                            var deserializedMockData = JsonSerializer.Deserialize<MockData<K>>(data)?.Data;

                            UpdateDataValuesAndInsertData(entity, deserializedMockData!);

                            break;
                        }
                        catch (JsonException ex)
                        {
                            _trace.Log($"last operation failed with error {ex.Message}. retry count {count} for last operation again.");

                            count++;
                            retryCount--;
                        }
                        catch
                        {
                            throw;
                        }
                    }
                }
            }
        }

        public async Task<List<K>?> GenerateData<K>(string locale, int noOfRows = 2, int openAiBatchSize = 5, string inDataValue = "", int retryCount = 3, params string[] coomonColumnsToIgnore) where K : class
        {
            int batchArrSize = noOfRows / openAiBatchSize;
            int remainder = noOfRows % openAiBatchSize;
            List<int> batchArr = new List<int>(remainder > 0 ? batchArrSize + 1 : batchArrSize);
            var entity = _analyser.AnalyseEntity<K>(_entityTypes);
            ChatCompletionOptions? completionOptions;
            List<K>? deserializedMockData = null;

            for (int i = 0; i < batchArrSize; i++)
            {
                batchArr.Add(openAiBatchSize);
            }

            if (remainder > 0)
            {
                batchArr.Add(remainder);
            }

            for (int i = 0; i < batchArr.Count(); i++)
            {
                var message = _generator.GenerateMessage(entity!, locale, out completionOptions, batchArr[i], inDataValue, coomonColumnsToIgnore);
                int count = 1;

                while (retryCount > 0)
                {
                    var data = await _generator.GenerateMockData(message, completionOptions);

                    if (!string.IsNullOrEmpty(data))
                    {
                        try
                        {
                            deserializedMockData = JsonSerializer.Deserialize<MockData<K>>(data)?.Data;

                            UpdateDataValues(entity, deserializedMockData!);

                            break;
                        }
                        catch (JsonException ex)
                        {
                            _trace.Log($"last operation failed with error {ex.Message}. retry count {count} for last operation again.");

                            count++;
                            retryCount--;
                        }
                        catch
                        {
                            throw;
                        }
                    }
                }
            }

            return deserializedMockData;
        }

        private void UpdateDataValues<K>(Entity entity, List<K> deserializedMockData) where K : class
        {
            GenericRepository<T, K> genericRepository = new GenericRepository<T, K>(_context);

            foreach (K data in deserializedMockData)
            {
                Type dataType = data.GetType();
                var dateTimeProperties = entity?.Properties?.Where(p => p.ClrTypeName == typeof(DateTime).Name).ToList();
                
                dateTimeProperties?.ForEach((property) =>
                {
                    var dateTimeProperty = dataType.GetProperty(property.Name!);

                    dateTimeProperty?.SetValue(data, DateTime.Now);
                });
            }
        }

        private void UpdateDataValuesAndInsertData<K>(Entity entity, IEnumerable<K> deserializedMockData) where K : class
        {
            GenericRepository<T, K> genericRepository = new GenericRepository<T, K>(_context);

            foreach (K data in deserializedMockData)
            {
                Type dataType = data.GetType();
                var dateTimeProperties = entity?.Properties?.Where(p => p.ClrTypeName == typeof(DateTime).Name).ToList();
                var foreignKeysProperties = entity?.ForeignKeys;
                var primaryKeyColumn = entity?.PrimaryKeys?.FirstOrDefault();
                var primaryKeyProperty = dataType.GetProperty(primaryKeyColumn!.Name!);

                if (primaryKeyColumn?.ClrTypeName == typeof(long).Name || primaryKeyColumn?.ClrTypeName == typeof(int).Name)
                {
                    primaryKeyProperty?.SetValue(data, 0);
                }

                dateTimeProperties?.ForEach((property) =>
                {
                    var dateTimeProperty = dataType.GetProperty(property.Name!);

                    dateTimeProperty?.SetValue(data, DateTime.Now);
                });

                foreignKeysProperties?.ForEach((property) =>
                {
                    var principals = property.Principals!;

                    if (principals.LastOrDefault() != principals.FirstOrDefault())
                    {
                        var principalEntity = _generatedEntities.Where(ge => ge?.DisplayName == principals.LastOrDefault()).FirstOrDefault();
                        var mockData = principalEntity?.MockData;
                        var primaryProperty = principalEntity?.PrimaryKeys?.FirstOrDefault();
                        var foreignProperty = dataType.GetProperty(property.Name!);
                        var count = mockData?.Count ?? 0;

                        if (count > 0)
                        {
                            int index = _random.Next(0, count - 1);
                            var principalData = mockData?[index];
                            var principalKeyProperty = (PropertyInfo?)principalData?.GetType().GetProperty(primaryProperty!.Name);
                            var value = principalKeyProperty?.GetValue(principalData, null);

                            foreignProperty?.SetValue(data, value);
                        }
                        else
                        {
                            string errorMsg = $"foreign key {property.Name} in table {entity?.DisplayName} value could not be updated as table {principals.LastOrDefault()} doesn't have data.";

                            throw new Exception(errorMsg);
                        }
                    }
                });
            }


            var typeCastedMockData = deserializedMockData?.ToList<dynamic>();

            entity!.MockData = typeCastedMockData;
            _generatedEntities.Add(entity!);
        }
    }
}