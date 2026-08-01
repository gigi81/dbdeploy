[![NuGet Version](https://img.shields.io/nuget/v/dbdeploy)](https://www.nuget.org/packages/dbdeploy)
[![Renovate enabled](https://img.shields.io/badge/renovate-enabled-brightgreen.svg)](https://renovatebot.com/)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/gigi81/dbdeploy/ci.yml)](https://github.com/gigi81/dbdeploy/actions)
[![codecov](https://codecov.io/github/gigi81/dbdeploy/graph/badge.svg?token=77BVOTN1X1)](https://codecov.io/github/gigi81/dbdeploy)

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

## Dry run

Both `deploy` and `rollback` accept `--dryrun`, which reports the scripts that would run without
changing anything:

```shell
dbdeploy deploy --path examples/examples01 --branch release/1.1 --dryrun
```

No deploy or rollback script is executed, no migration is recorded or removed, the migrations table
is not created, and neither `--create` nor `--update` is acted on: a database that does not exist is
reported as an error rather than created. The databases still have to be reachable, because the plan
is worked out by comparing the branch with the migrations they already have.

The `ci` verb passes the flag on to the `deploy` and `rollback` commands it runs.

## Update the branch files after a release

Once a release branch has been deployed, its scripts belong to the default branch (see
[main.csv](#maincsv) below). Passing `--update` does that bookkeeping for you: after the
deployment has succeeded, the steps of the branch are appended to `main.csv`, the branch file is
deleted, and any `@include` of it left in the other branch files is removed.

```shell
dbdeploy deploy --path examples/examples01 --branch release/1.1 --update
```

A branch that includes another one is deployed together with it, so both are moved and both files
are deleted. Deploying the default branch with `--update` does nothing, since there is nothing to
move.

The files are only changed on disk: reviewing and committing the result is up to you. With
`--dryrun` nothing is written and the move is only logged.

## Deploy during Development/CI
This command will create databases (if they don't already exits) and deploy both .Deploy.sql scripts and .Test.sql scripts
```shell
dbdeploy deploy --path examples/examples01 --branch release/1.1 --create --test
```

## Pre and post scripts

A database can have a script that runs **around** a deploy or a rollback, rather than as part of
it: taking a backup, disabling constraints or jobs, refreshing statistics, and so on. There are four
of them and all are optional, off unless you name them:

```json
{
  "global": {
    "preDeploy": "_PreDeploy",
    "postDeploy": "_PostDeploy",
    "preRollback": "_PreRollback",
    "postRollback": "_PostRollback"
  },
  "databases":{
    "Database01": {
      "connectionString": "...",
      "preDeploy": "_Database01PreDeploy"
    }
  }
}
```

The setting holds the name of the script, without the `.sql` extension, exactly like
`global:initStepName`. A name set on a database overrides the global one for that database.

The file is looked up in the **database folder first and in the root folder after**, so one script
can be shared by every database and still be overridden by one of them:

```shell
/Database01/_PreDeploy.sql    <- used for Database01
/_PreDeploy.sql               <- used for every other database
```

The rules around them:

- A configured script that exists in neither place is an error, and `deploy`, `rollback` and
  `validate` all fail on it before touching any database.
- They run only for the databases that have something to deploy or to rollback. A database whose
  steps are all deployed already runs neither its pre nor its post script.
- If a **pre** script fails, nothing is deployed or rolled back.
- If a **post** script fails, it is logged as an error and the run carries on with whatever is left
  to do, including `--update`; the command then exits with the number of post scripts that failed.
- They are not migrations: nothing is recorded for them and they run again on every deploy or
  rollback that has work for their database.
- With `--dryrun` they are only reported, like every other script.

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
This can be done automatically by deploying with [`--update`](#update-the-branch-files-after-a-release).

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

## Format

To format the SQL of every `.Deploy.sql` and `.Rollback.sql` script consistently, run:

```shell
dbdeploy format --path examples/examples01
```

Every branch is formatted. `_Init` scripts are never touched, because they are generated schema
dumps and reformatting one produces an enormous diff of something nobody reads by hand.

`format` **never connects to a database**. It works entirely off what is on disk — the scripts, the
branch files, `.editorconfig` and `dbsettings.json` — and reads the settings file only to learn
which dialect each database is written in, so no connection string has to be valid, or present at
all. If the settings say nothing about a database, `--provider` (then `global:defaultProvider`)
decides the dialect.

To format scripts that are not part of a branch — or a folder that is not a `dbdeploy` layout at
all — pass one or more globs with `--include`:

```shell
dbdeploy format --path ./db --include "**/*.sql"
dbdeploy format --path ./scratch --include "**/*.sql" --provider oracle
dbdeploy format --path ./db --include "**/*.Test.sql" --exclude "**/legacy/**"
```

Globs are relative to `--path`, and several can follow one flag separated by spaces
(`-i "**/*.sql" "setup/*.ddl"`). With `--include` the branch structure is not read either, and
nothing is filtered out — init scripts included.

Only `*`, `**` and `?` are supported. A character class such as `[Ii]` is **not**, and matches
nothing rather than reporting an error, so write two patterns instead:

```shell
dbdeploy format --path ./db --include "**/*.sql" --exclude "**/_Init*" "**/_init*"
```

Note that init scripts are generated schema dumps and can be very large; excluding them is usually
what you want in directory mode.

The dialect is then worked out per file: the nearest folder above it named after a configured
database wins, so a normal layout still formats each database in its own dialect without any extra
flag. Failing that, `--provider` is used, then `global:defaultProvider`. If none of those apply the
command stops and lists the providers it knows. `dbsettings.json` is optional for `format`, so a
loose folder of scripts works with nothing but `--provider`.

The formatter is driven by the **script repository's own `.editorconfig`**, resolved from each
script's directory, and it understands the dialect of the database the script belongs to.

Every formatted file is logged with the dialect it was laid out with (`Formatted …/TKT-001.Deploy.sql
as sqlServer`), so a script that picked up the wrong dialect from the folder layout is visible in the
run rather than only in the diff.

### Released scripts are left alone

The migration hash is the MD5 of the deploy script, so rewriting one that has already been deployed
stops it matching what the database recorded. For this reason released scripts
(scripts listed in the main branch file) are not formatted.
This can be overridden with `--force` but will cause warnings during deployment since the hash
will no longer match the value in the migration table.

```shell
dbdeploy format --path examples/examples01 --force
```

Directory mode (`--include`) knows nothing about branches, so it formats whatever the globs match,
released or not.

Formatting is checked before anything is written: the result is tokenised again and compared against
the source, and a file whose significant tokens changed is left alone and reported as a failure
(`dbdeploy format` then exits non-zero). Layout, trailing whitespaces, keyword casing and comment
indentation may change. Identifiers, string literals and comments content never do.


### Formatting Configuration

Standard `.editorconfig` properties are honored for `*.sql`:

```ini
[*.sql]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
insert_final_newline = true
max_line_length = 120
```

`indent_style`/`indent_size` set one indentation level, `end_of_line` the line endings (with no
setting, each file keeps the endings it already has), `charset` the encoding, and `max_line_length`
the width above which a parenthesised group is broken over several lines instead of being kept
inline.

`trim_trailing_whitespace` needs no work in the laid-out code, which never ends a line with
whitespace; it controls whether the continuation lines of a block comment are trimmed as they are
re-indented. Trailing whitespace inside a string literal is content and is never touched.

Alongside those, these `dbdeploy`-specific properties are available:

| Property | Values | Default | Effect |
| --- | --- | --- | --- |
| `dbdeploy_sql_enabled` | `true`, `false` | `true` | Set to `false` to exempt a glob from formatting entirely |
| `dbdeploy_sql_keyword_case` | `upper`, `lower`, `preserve` | `upper` | Casing of keywords |
| `dbdeploy_sql_data_type_case` | as above | follows `keyword_case` | Casing of data types |
| `dbdeploy_sql_function_case` | as above | `upper` | Casing of built-in functions only; your own routines keep the casing you gave them |
| `dbdeploy_sql_batch_separator_case` | as above | `upper` | Casing of `GO` |
| `dbdeploy_sql_blank_lines_between_statements` | a number | `1` | Blank lines between statements |

For example, to leave a folder of vendor scripts alone:

```ini
[vendor/**.sql]
dbdeploy_sql_enabled = false
```

### Formatting rules

Clause keywords go on a line of their own with their body indented; joins and boolean connectives
start a new line and keep their operands beside them:

```sql
SELECT
    Field1,
    Field2
FROM
    TABLE1 t1
    INNER JOIN TABLE2 t2 ON t1.id = t2.id
WHERE
    Field1 = 'a'
    AND Field2 = 'b'
```

Statement keywords keep their object name inline, and a `CREATE` or `ALTER` column list becomes a
block:

```sql
CREATE TABLE dbo.Customers
(
    CustomerId INT NOT NULL IDENTITY(1, 1),
    FirstName NVARCHAR(30) NULL,
    CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (CustomerId)
)
GO
```

Procedural code is indented by block, and the things that are not SQL — SQL\*Plus directives,
MySQL `DELIMITER` statements, batch separators — are reproduced exactly as written:

```sql
CREATE OR REPLACE PROCEDURE secure_dml
IS
BEGIN
    IF TO_CHAR(SYSDATE, 'HH24:MI') NOT BETWEEN '08:00' AND '18:00'
        OR TO_CHAR(SYSDATE, 'DY') IN ('SAT', 'SUN') THEN
        RAISE_APPLICATION_ERROR(-20205, 'Only during office hours');
    END IF;
END secure_dml;
/
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