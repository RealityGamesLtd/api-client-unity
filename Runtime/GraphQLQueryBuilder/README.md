This is a modified solution for building GraphQL queries based on: https://github.com/Revmaker/Getit.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) ![VSTS Build Status](https://carlabs.visualstudio.com/Getit/_apis/build/status/Getit-PR)


## Usage
```csharp
IQuery userQuery = new Query();
userQuery
    .Name("User")
    .Select("userId", "firstName", "lastName", "phone")
    .Where("userId", "331")
    .Where("lastName", "Calhoon")
    .Comment("My First Query");
```

Let's breakdown a simple GraphQL query and write it in querybuilder.
```csharp
{
    NearestDealer(zip: "91403", make: "aston martin") {
    distance
    rating
    Dealer {
      name
      address
      city
      state
      zip
      phone
    }
  }
}
```
This query has a simple set of parameters, and a select field, along with what I'll call a
**sub-select**. Query Name is `NearestDealer` with the follwing parameters `zip` and `make`.
Both are of string type, although they can be any GraphQL type including enums.

Now lets write this with the Querybuilder
```csharp
subSelectDealer
    .Name("Dealer")
    .Select("name", "address", "city", "state", "zip", "phone");
nearestDealerQuery
    .Name("NearestDealer")
    .Select("distance")
    .Select("rating")
    .Select(subSelectDealer)
    .Where("zip", "91302")
    .Where("make", "aston martin");
```
It's pretty straight forward. You can also pass in dictionary type objects to both the `Select` and 
the `Where` parts of the statement. Here is another way to write the same query -
```csharp
Dictionary<string, object> whereParams = new Dictionary<string, object>
{
    {"make", "aston martin"},
    {"zip",  "91403"}
};
List<object> selList = new List<object>(new object[] {"distance", "rating", subSelectDealer});
nearestDealerQuery
    .Name("NearestDealer")
    .Select(selList)
    .Where(whereParams);
```
When appropriate (You figure it out) you can use the following data types, 
which can also contain nested data types, so you can have an list of strings 
and such nested structures.

These C# types are supported:
* `string`
* `int`
* `float`
* `double`
* `EnumHelper` (For enumerated types)
* `KeyValuePair<string, object>`
* `IList<object>`
* `IDictionary<string, object>`

#### A more complex example with nested parameter data types
```csharp
// Create a List of Strings for passing as an ARRAY type parameter
List<string> modelList = new List<string>(new[] {"DB7", "DB9", "Vantage"});
List<object> recList = new List<object>(new object[] {"rec1", "rec2", "rec3"});
// Here is a From/To parameter object
Dictionary<string, object> recMap = new Dictionary<string, object>
{
    {"from", 10},
    {"to",   20},
};
// try a more complicate dict with sub structs, list and map
Dictionary<string, object> fromToPrice = new Dictionary<string, object>
{
    {"from", 88000.00},
    {"to", 99999.99},
    {"recurse", recList},
    {"map", recMap}
};
// Now collect them all up in a final dictionary and it will traverse
// and build the GraphQL query
Dictionary<string, object> myWhere = new Dictionary<string, object>
{
    {"make", "aston martin"},
    {"state", "ca"},
    {"limit", 2},
    {"trims", trimList},
    {"models", modelList},
    {"price", fromToPrice}
};
List<object> selList = new List<object>(new object[] {"id", subSelect, "name", "make", "model"});
// Now finally build the query
query
    .Name("DealerInventory")
    .Select("some_more", "things", "in_a_select")   // another way to set selects 
    .Select(selList)                                // select list object
    .Where(myWhere)                                 // add the nested Parameters
    .Where("id_int", 1)                             // add some more
    .Where("id_double", 3.25)
    .Where("id_string", "some_sting_id")
    .Comment("A complicated GQL Query");
```

### Multiple `Query` Query
```csharp
IQuery nearestDealerQuery = new Query();
IQuery subSelectDealer = new Query();
// A sub-select build the same way a query is
subSelectDealer
    .Name("Dealer")
    .Select("name", "address", "city", "state", "zip", "phone");
nearestDealerQuery
    .Name("NearestDealer")
    .Select("distance")
    .Select(subSelectDealer)
    .Where("zip", "91302")
    .Where("make", "aston martin");
// Dump the generated query
Console.WriteLine(nearestDealerQuery);
```
### Raw GraphQL query
While the query builder is helpful, their are some cases where it's just simpler
to pass a raw or prebuilt query to the GraphQL server. This is acomplished by using the *Raw()* query method.
In this example we have on the server a query that responds to a `Version` number request.

The GraphQL JSON response from the `Make` query would look like this -
```json
{
  "Make": [
    {
      "id": 121,
      "name": "aston martin"
    }
  ]
}
```

#### Example RAW query code
```csharp
    // Dispense a query
    IQuery aQuery = new Query();
    // Set the RAW GraphQL query
    aQuery.Raw(@"{Make(name: "Kia") {id name }}");
```
#### RAW Example Console Output
```json
{
  "data": {
    "Alias": [
      {
        "id": 121,
        "name": "Aston Martin"
      }
    ]
  }
}
```

### Batched Queries
With GraphQL you can send batches of queries in a single request. 
Essentiall you can pass any generated query to the `Batch()` method and it will be stuffed into
the call. Using JObject's or JSON string returns from `Get()` is usually how you get the 
blob of data back from batched queries.
```csharp
nearestDealerQuery
    .Name("NearestDealer")
    .Select("distance")
    .Select(subSelectDealer)
    .Where("zip", "91302")
    .Where("make", "aston martin");
batchQuery.Raw("{ Version }");          // Get the version
batchQuery.Batch(nearestDealerQuery);   // Batch up the nearest dealer query
```

### Query Alias
Sometimes it's handy to be able to call a query but have it respond with a different
name. Generally if You call a GraphQL query with a specific name, you get that back as 
the resulting data set element. This can be a problem with batch queries hitting the same
endpoint. Or if you want to have the name of the data object being returned 
just be different. The proper name of the 
query should be set in the Name() method, but additionally you can add an `Alias()` call
to set that. So back to a simple example query with an alias-

```csharp
{
    AstonNearestDealer:NearestDealer(zip: "91403", make: "aston martin") {
    distance
    rating
    Dealer {
      name
      address
      city
      state
      zip
      phone
    }
  }
}
```
This query has a simple set of parameters, and a select field, along with what I'll call a
**sub-select**. Query Name is `NearestDealer` with the follwing parameters `zip` and `make`.
Both are of string type, although they can be any GraphQL type including enums.

```csharp
subSelectDealer
    .Name("Dealer")
    .Select("name", "address", "city", "state", "zip", "phone");
nearestDealerQuery
    .Name("NearestDealer")
    .Alias("AstonNearestDealer")
    .Select("distance")
    .Select("rating")
    .Select(subSelectDealer)
    .Where("zip", "91302")
    .Where("make", "aston martin");
```
The response (JObject) would be something like this -
```json
{
  "AstonNearestDealer": [
    {
      "distance": 7.5,
      "Dealer": {
        "name": "Randy Butternubs Aston Martin",
        "address": "1234 Haystack Calhoon Road",
        "city": "Hank Kimballville",
        "state": "CA",
        "zip": "91302",
        "phone": "(818) 887-7111",
      }
    }
  ]
}
...
```
### GraphQL Enums
When working with GraphQL you have a few different datatypes. One issue with the
Query builder is that their would be no way to differentiate a string and an Enumeration.
It uses the data type to determine how to build the query so in cases where you need an real 
GraphQL enumerations their is a simple helper class that can be used.

Example how to generate an Enumeration
```csharp
EnumHelper GqlEnumEnabled = new EnumHelper().Enum("ENABLED");
EnumHelper GqlEnumDisabled = new EnumHelper("DISABLED");
EnumHelper GqlEnumConditionNew = new EnumHelper("NEW");
EnumHelper GqlEnumConditionUsed = new EnumHelper("USED");
```

Example creating a dictionary for a select (GraphQL Parameters)
```csharp
Dictionary <string, object> mySubDict = new Dictionary<string, object>;
{
    {"Make", "aston martin"},
    {"Model", "DB7GT"},
    {"Condition", GqlEnumConditionNew}, // Used it in a Dictionary
};
query.Name("CarStats")
    .Select("listPrice", "horsepower", "color")
    .Where(myDict)
    .Where("_debug", GqlEnumDisabled)       // Used it in a where
    .Comment("Using Enums");
```
This will generate a query that looks like this (well part of it anyway)
```csharp
{
    CarStats(Make:"aston martin", Model:"DB7GT", Condition:NEW, _debug:DISABLED)
    { 
        listPrice
        horsepower
        color
    }
}
```