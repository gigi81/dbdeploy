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

ALTER VIEW "Customer and Suppliers by City" AS
SELECT City, CompanyName, ContactName, 'Customers' AS Relationship
FROM Customers
UNION SELECT City, CompanyName, ContactName, 'Suppliers'
FROM Suppliers
GO

ALTER TABLE Customers
DROP COLUMN FirstName,LastName
GO