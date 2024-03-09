> [!CAUTION]
> The tool is in early stage of development. Use with caution. Constructive feedback is welcome.

![NuGet Version](https://img.shields.io/nuget/v/dbdeploy)
[![Renovate enabled](https://img.shields.io/badge/renovate-enabled-brightgreen.svg)](https://renovatebot.com/)
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/gigi81/dbdeploy/ci.yml)

# dbdeploy

**dbdeploy** is a cli tool to deploy and rollback single or multiple databases changes during all phases of development from the local developer machine to production.

## Install
```shell
dotnet tool install --global dbdeploy
```

## Deploy
```shell
dbdeploy deploy --path examples/examples01 --branch release/1.1
```

## Rollback
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
For example scripts are not numbered sequentially but have unique names. In this way two developers working on different branches will not have clashing numbers when they will merge their respective feature branches but at worst, if working on the same release, will have to deal with merging changes on a csv file which contains the ordered sequence of scripts that needs deploying.   
The tools supports both **deploy** and **rollback** scripts along with **test** scripts that can be used to load test data both during development or for your integration tests.

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
This file contains the lists of databases connection strings and settings for the tool to be able to connect to apply the changes. You can have multiple settings files, one for each **environment**.
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

### main.csv
This file contains the list of scripts that are deployed to production. After each successful release, developers should move the list of deployed scripts to this file.
It is recommended for this file name to match your main branch name (if any).
```
Database01,_Init
Database02,_Init
```

### release_1.1.csv
This is a sample release file. This contains the list of the files to deploy for a sample release. The sequence implicitly include the scripts from **main.csv**.
It is recommended for this file name to match your release branch name (if any) where the '/' is replaced with a '_'
```
Database01,TKT-001.SampleDescription
```

### release_1.2.csv
This is a sample release file. This contains the list of the files to deploy for a sample release. The sequence explicitly includes scripts from **release_1.1** and implicitly include the scripts from **main.csv**
It is recommended for this file name to match your release branch name (if any) where the '/' is replaced with a '_'
```
@include release_1.1
Database02,TKT-002.SampleDescription
```
