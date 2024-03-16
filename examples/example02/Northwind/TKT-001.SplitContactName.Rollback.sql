ALTER TABLE Customers
    ADD ContactName nvarchar(30) NULL
GO

UPDATE
    Customers
SET
    ContactName = FirstName + ' ' +  LastName
GO

ALTER TABLE Customers
ALTER COLUMN ContactName nvarchar(30) NOT NULL
GO

ALTER TABLE Customers
DROP COLUMN FirstName,LastName
GO