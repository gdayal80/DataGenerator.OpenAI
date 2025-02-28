
![Nuget](https://img.shields.io/nuget/v/DataGenerator.EntityFramework.Core)

# Introduction
DataGenerator.EntityFramework.Core is a package to generate Mock Data using OpenAI & EntityFramework Core on Windows / Linux / MacOS. The package is compatible only with primitive DataTypes, Complex DataTypes are not supported.

# Parameter: `openAiBatchSize` 

`openAiBatchSize` should be equal to `noOfRows` in case you want to ensure unique data values.

# Parameter: `inDataValue` 

`inDataValue` is the value of the data under which you want to generate data. For instance under state `Maharashtra` you want to generate data for table `City` then pass `inDataValue` as `maharashtra`.

# Getting started

From nuget packages

![Nuget](https://img.shields.io/nuget/v/DataGenerator.EntityFramework.Core)

`PM> Install-Package DataGenerator.EntityFramework.Core`

## Usage 

```C#
using DataGenerator.EntityFrameworkCore.Interfaces;
    
public class ConsoleTraceWriter : ITraceWriter
{
    public void Log(string message)
    {
        Console.WriteLine(message);
    }

    public void Verbose(string message)
    {
        Console.WriteLine(message);
    }
}
```
```C#
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Collections.Generic;
using DataGenerator.EntityFrameworkCore.Mock.Data.Generators;
using DataGenerator.EntityFrameworkCore.Data.Generators;
using System.Globalization;

Random random = new Random();
var connStr = Environment.GetEnvironmentVariable("LOCALHOST_MYSQL")!;
var dbOptions = new DbContextOptionsBuilder<Context>().UseMySql(connStr, ServerVersion.AutoDetect(connStr),
                    mySqlOptionsAction: (MySqlDbContextOptionsBuilder sqlOptions) =>
                    {
                        sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 10,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                        sqlOptions.CommandTimeout(240);
                    }).ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning)).Options;
var context = new Context(dbOptions);
var trace = new ConsoleTraceWriter();
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
string locale = CultureInfo.CurrentCulture.Name;

try
{
    MockDataGenerator mockDataGenerator = new MockDataGenerator(trace, openAiApiKey);
    EntityFrameworkDataGenerator<Context> entityFrameworkDataGenerator = new EntityFrameworkDataGenerator<Context>(context, mockDataGenerator, trace);

    var users = await entityFrameworkDataGenerator.GenerateData<User>(locale, 5, 5);

    context.Users.AddRange(users!);

    var schools = await entityFrameworkDataGenerator.GenerateData<School>(locale, 1, 1);

    foreach (var school in schools!)
    {
        school.CreatedBy = users?[random.Next(0, users.Count())];
        school.UpdatedBy = users?[random.Next(0, users.Count())];
    }

    context.Schools.AddRange(schools!);

    var schoolBranches = await entityFrameworkDataGenerator.GenerateData<SchoolBranch>(locale, 5, 5, schools?[0].SchoolName!);

    foreach (var schoolBranch in schoolBranches!)
    {
        schoolBranch.School = schools?[0];
        schoolBranch.CreatedBy = users?[random.Next(0, users.Count())];
        schoolBranch.UpdatedBy = users?[random.Next(0, users.Count())];
    }

    context.SchoolBranches.AddRange(schoolBranches!);

    var countries = await entityFrameworkDataGenerator.GenerateData<Country>(locale, 1, 1);

    foreach (var country in countries!)
    {
        country.SchoolBranch = schoolBranches?[random.Next(0, schoolBranches.Count())];
        country.CreatedBy = users?[random.Next(0, users.Count())];
        country.UpdatedBy = users?[random.Next(0, users.Count())];
    }

    context.Countries.AddRange(countries!);

    var states = await entityFrameworkDataGenerator.GenerateData<State>(locale, 25, 25);

    foreach (var state in states!)
    {
        state.Country = countries?[0];
        state.CreatedBy = users?[random.Next(0, users.Count())];
        state.UpdatedBy = users?[random.Next(0, users.Count())];
    }

    context.States.AddRange(states!);

    var cities = new List<City>();

    foreach (var state in states!)
    {
        var citiesInState = await entityFrameworkDataGenerator.GenerateData<City>(locale, 25, 25, state.Name!);

        citiesInState?.ForEach((city) =>
        {
            city.State = state;
            city.CreatedBy = users?[random.Next(0, users.Count())];
            city.UpdatedBy = users?[random.Next(0, users.Count())];
        });
        cities.AddRange(citiesInState!);
    }

    context.Cities.AddRange(cities!);

    var addressTypes = await entityFrameworkDataGenerator.GenerateData<AddressType>(locale, 2, 2);

    foreach (var addressType in addressTypes!)
    {
        addressType.SchoolBranch = schoolBranches?[random.Next(0, schoolBranches.Count())];
        addressType.CreatedBy = users?[random.Next(0, users.Count())];
        addressType.UpdatedBy = users?[random.Next(0, users.Count())];
    }

    context.AddressTypes.AddRange(addressTypes!);

    var addresses = new List<Address>();

    foreach (var city in cities!)
    {
        var addressesInCity = await entityFrameworkDataGenerator.GenerateData<Address>(locale, 125, 125, city.Name!);

        foreach (var address in addressesInCity!)
        {
            address.AddressType = addressTypes?[random.Next(0, addressTypes.Count())];
            address.City = city;
            address.CreatedBy = users?[random.Next(0, users.Count())];
            address.UpdatedBy = users?[random.Next(0, users.Count())];
        }

        addresses.AddRange(addressesInCity);
    }

    context.Addresses.AddRange(addresses!);

    await context.SaveChangesAsync();
}
catch (Exception ex)
{
    trace.Log(ex.Message);
}
```

# Third Parties
* [Microsoft.EntityFrameworkCore](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/8.0.12)
* [OpenAI](https://www.nuget.org/packages/OpenAI/2.1.0)
