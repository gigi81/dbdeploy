ALTER TABLE Customers
ADD FirstName nvarchar(30) NULL,
    LastName nvarchar(30) NULL
GO

UPDATE
    Customers
SET
    FirstName = SUBSTRING(ContactName, 1, CHARINDEX(' ', ContactName) - 1),
    LastName = SUBSTRING(ContactName, CHARINDEX(' ', ContactName) + 1, LEN(ContactName))
GO

ALTER TABLE Customers
ALTER COLUMN FirstName nvarchar(30) NOT NULL
GO

ALTER TABLE Customers
ALTER COLUMN LastName nvarchar(30) NOT NULL
GO

ALTER TABLE Customers
DROP COLUMN ContactName
GO