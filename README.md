[![NuGet Version](https://img.shields.io/nuget/v/dbdeploy)](https://www.nuget.org/packages/dbdeploy)
[![Renovate enabled](https://img.shields.io/badge/renovate-enabled-brightgreen.svg)](https://renovatebot.com/)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/gigi81/dbdeploy/ci.yml)](https://github.com/gigi81/dbdeploy/actions)

# dbdeploy

**dbdeploy** is an opinionated cli tool to deploy and rollback single or multiple databases changes during all phases of development from the local developer machine to production.

## Install
`dbdeploy` is built as a dotnet tool and so it requires the .NET SDK to be installed in the system.
Even if the tool is built with dotnet, you can use it alongside any other language and framework.

You can [download the .NET SDK from here](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

Once the sdk is installed, run this command to install the tool.

```shell
dotnet tool install --global dbdeploy
```

## Update

To update the tool to the latest available version, run:

```shell
dotnet tool update --global dbdeploy
```

## Examples

In the `examples` folder of the git repository you can find some sample databases that are also used during
the integration testing of the tool. These are samples taken
from other sources like for example `Northwind` for Sql Server and `Pagila` for PostgreSQL.
Please read their respective readme for details on their licenses.

## Deploy

To deploy database changes, run:

```shell
dbdeploy deploy --path examples/examples01 --branch release/1.1
```

## Rollback

To rollback database changes previously deployed, run:

```shell
dbdeploy rollback --path examples/examples01 --branch release/1.1
```

## Deploy during Development/CI
This command will create databases (if they don't already exits) and deploy both .Deploy.sql scripts and .Test.sql scripts
```shell
dbdeploy deploy --path examples/examples01 --branch release/1.1 --create --test
```

## Files structure
The files structure and content is designed to play nice with source control systems like git.

For example scripts are not numbered sequentially but have unique names.
In this way two developers working on different branches will not have clashing numbers when they will merge
their respective feature branches but, at worst, will have to deal with conflicts on the csv file that contains the
ordered sequence of scripts that needs deploying.

The tools support both **deploy** and **rollback** scripts along with (optional) **test** scripts that can be used to load test
data during development or for your integration tests and also (optional) **data** scripts that can be used to load data
for example to prime a database table. 



## Sample structure of files

```shell
/db1/
  _Init.Sql
  TKT-001.SampleDescription.Deploy.sql
  TKT-001.SampleDescription.Rollback.sql
  ...
/db2/
  _Init.Sql
  TKT-002.SampleDescription.Deploy.sql
  TKT-002.SampleDescription.Rollback.sql
  ...
dbsettings.json
main.csv
release_1.1.csv
release_1.2.csv
```

### dbsettings.json
This file contains the lists of databases connection strings and settings along with the tool global settings
to enable the tool to connect to the database(s).

You can have multiple settings files, one for each **environment**, so that you can override some settings for the
specific environment like the connection strings. 

```json
{
  "global": {
    "defaultProvider": "sqlServer"
  },
  "databases":{
    "Database01": {
      "connectionString": "..."
    },
    "Database02": {
      "connectionString": "...",
      "provider": "mysql"
    }
  }
}
```

## Branches

### main.csv
This file contains the list of scripts that are deployed to production.

**After each successful release, developers should move the list of deployed scripts to this file.**

It is recommended for this file name to match your `main` branch name which could be for example `develop`.

```
Database01,_Init
Database02,_Init
```

### release_1.1.csv
This is a sample release file. This contains the list of the files to deploy for a sample release branch `release/1.1`.

The sequence implicitly include the scripts from `main.csv`.

It is recommended for this file name to match your release branch name (if any) where the '/' is replaced with a '_'.
```
Database01,TKT-001.SampleDescription
```

### release_1.2.csv
This is a sample release file. This contains the list of the files to deploy for a sample release branch `release/1.2`.

The sequence explicitly includes scripts from `release_1.1.csv`, by using the keyword `@include`,
and also implicitly includes the scripts from `main.csv`.

It is recommended for this file name to match your release branch name (if any) where the '/' is replaced with a '_'.

```
@include release_1.1
Database02,TKT-002.SampleDescription
```

## Alternatives

A list of other database migration tools, both open source and commercial.

[DbUp](https://dbup.readthedocs.io/en/latest/)

[Grate](https://erikbra.github.io/grate/)

[migrate](https://github.com/golang-migrate/migrate?tab=readme-ov-file)

[dbmate](https://github.com/amacneil/dbmate)

[FlyWay](https://flywaydb.org/)

[Migrator.Net](https://github.com/migratordotnet/Migrator.NET)

[Liquidbase](https://www.liquibase.com/)

[Atlas](https://atlasgo.io/)

[Nasgrate](https://github.com/dlevsha/nasgrate)