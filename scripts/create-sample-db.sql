-- SchemaDoc Sample Database: Online Bookstore
USE master;
GO

IF DB_ID('BookstoreDB') IS NOT NULL
    ALTER DATABASE BookstoreDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
GO
IF DB_ID('BookstoreDB') IS NOT NULL
    DROP DATABASE BookstoreDB;
GO

CREATE DATABASE BookstoreDB;
GO
USE BookstoreDB;
GO

-- ══════════════════════════════════════════════════════════════
-- SCHEMAS
-- ══════════════════════════════════════════════════════════════
CREATE SCHEMA Catalog;
GO
CREATE SCHEMA Sales;
GO
CREATE SCHEMA Membership;
GO

-- ══════════════════════════════════════════════════════════════
-- Membership schema
-- ══════════════════════════════════════════════════════════════
CREATE TABLE Membership.Customers (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    FirstName   NVARCHAR(100)  NOT NULL,
    LastName    NVARCHAR(100)  NOT NULL,
    Email       NVARCHAR(255)  NOT NULL UNIQUE,
    Phone       NVARCHAR(20)   NULL,
    JoinedAt    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    IsActive    BIT            NOT NULL DEFAULT 1
);
GO

EXEC sp_addextendedproperty @name=N'MS_Description', @value=N'Registered customers',
    @level0type=N'SCHEMA', @level0name=N'Membership',
    @level1type=N'TABLE',  @level1name=N'Customers';
GO
EXEC sp_addextendedproperty @name=N'MS_Description', @value=N'Unique email for login',
    @level0type=N'SCHEMA', @level0name=N'Membership',
    @level1type=N'TABLE',  @level1name=N'Customers',
    @level2type=N'COLUMN', @level2name=N'Email';
GO

CREATE TABLE Membership.Addresses (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId  INT            NOT NULL REFERENCES Membership.Customers(Id),
    Line1       NVARCHAR(200)  NOT NULL,
    Line2       NVARCHAR(200)  NULL,
    City        NVARCHAR(100)  NOT NULL,
    State       NVARCHAR(50)   NULL,
    PostalCode  NVARCHAR(20)   NOT NULL,
    Country     NVARCHAR(50)   NOT NULL DEFAULT 'US',
    IsDefault   BIT            NOT NULL DEFAULT 0
);
GO

-- ══════════════════════════════════════════════════════════════
-- Catalog schema
-- ══════════════════════════════════════════════════════════════
CREATE TABLE Catalog.Publishers (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Name        NVARCHAR(200)  NOT NULL,
    Country     NVARCHAR(50)   NULL,
    Website     NVARCHAR(500)  NULL
);
GO

CREATE TABLE Catalog.Authors (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    FirstName   NVARCHAR(100)  NOT NULL,
    LastName    NVARCHAR(100)  NOT NULL,
    Bio         NVARCHAR(MAX)  NULL,
    BornYear    INT            NULL
);
GO

EXEC sp_addextendedproperty @name=N'MS_Description', @value=N'Author biography or summary',
    @level0type=N'SCHEMA', @level0name=N'Catalog',
    @level1type=N'TABLE',  @level1name=N'Authors',
    @level2type=N'COLUMN', @level2name=N'Bio';
GO

CREATE TABLE Catalog.Categories (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Name        NVARCHAR(100)  NOT NULL UNIQUE,
    Description NVARCHAR(500)  NULL,
    ParentId    INT            NULL REFERENCES Catalog.Categories(Id)
);
GO

EXEC sp_addextendedproperty @name=N'MS_Description', @value=N'Self-referencing hierarchy of book categories',
    @level0type=N'SCHEMA', @level0name=N'Catalog',
    @level1type=N'TABLE',  @level1name=N'Categories';
GO

CREATE TABLE Catalog.Books (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Title       NVARCHAR(300)  NOT NULL,
    ISBN        NVARCHAR(20)   NOT NULL UNIQUE,
    PublisherId INT            NOT NULL REFERENCES Catalog.Publishers(Id),
    CategoryId  INT            NOT NULL REFERENCES Catalog.Categories(Id),
    Price       DECIMAL(10,2)  NOT NULL,
    Pages       INT            NULL,
    PublishedAt DATE           NULL,
    InStock     BIT            NOT NULL DEFAULT 1,
    CreatedAt   DATETIME2      NOT NULL DEFAULT GETUTCDATE()
);
GO

EXEC sp_addextendedproperty @name=N'MS_Description', @value=N'Main product catalog of books',
    @level0type=N'SCHEMA', @level0name=N'Catalog',
    @level1type=N'TABLE',  @level1name=N'Books';
GO
EXEC sp_addextendedproperty @name=N'MS_Description', @value=N'International Standard Book Number',
    @level0type=N'SCHEMA', @level0name=N'Catalog',
    @level1type=N'TABLE',  @level1name=N'Books',
    @level2type=N'COLUMN', @level2name=N'ISBN';
GO

-- Many-to-many: Books ↔ Authors
CREATE TABLE Catalog.BookAuthors (
    BookId      INT NOT NULL REFERENCES Catalog.Books(Id),
    AuthorId    INT NOT NULL REFERENCES Catalog.Authors(Id),
    CONSTRAINT PK_BookAuthors PRIMARY KEY (BookId, AuthorId)
);
GO

-- ══════════════════════════════════════════════════════════════
-- Sales schema
-- ══════════════════════════════════════════════════════════════
CREATE TABLE Sales.Orders (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId  INT            NOT NULL REFERENCES Membership.Customers(Id),
    AddressId   INT            NOT NULL REFERENCES Membership.Addresses(Id),
    OrderDate   DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
    Status      NVARCHAR(20)   NOT NULL DEFAULT 'Pending',
    TotalAmount DECIMAL(12,2)  NOT NULL DEFAULT 0,
    Notes       NVARCHAR(1000) NULL
);
GO

EXEC sp_addextendedproperty @name=N'MS_Description', @value=N'Pending → Processing → Shipped → Delivered → Cancelled',
    @level0type=N'SCHEMA', @level0name=N'Sales',
    @level1type=N'TABLE',  @level1name=N'Orders',
    @level2type=N'COLUMN', @level2name=N'Status';
GO

CREATE TABLE Sales.OrderItems (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    OrderId     INT            NOT NULL REFERENCES Sales.Orders(Id),
    BookId      INT            NOT NULL REFERENCES Catalog.Books(Id),
    Quantity    INT            NOT NULL DEFAULT 1,
    UnitPrice   DECIMAL(10,2)  NOT NULL
);
GO

CREATE TABLE Sales.Reviews (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    BookId      INT            NOT NULL REFERENCES Catalog.Books(Id),
    CustomerId  INT            NOT NULL REFERENCES Membership.Customers(Id),
    Rating      TINYINT        NOT NULL CHECK (Rating BETWEEN 1 AND 5),
    Title       NVARCHAR(200)  NULL,
    Body        NVARCHAR(MAX)  NULL,
    CreatedAt   DATETIME2      NOT NULL DEFAULT GETUTCDATE()
);
GO

-- ══════════════════════════════════════════════════════════════
-- VIEWS
-- ══════════════════════════════════════════════════════════════
CREATE VIEW Sales.vw_OrderSummary AS
SELECT
    o.Id            AS OrderId,
    c.FirstName + ' ' + c.LastName AS CustomerName,
    o.OrderDate,
    o.Status,
    o.TotalAmount,
    COUNT(oi.Id)    AS ItemCount
FROM Sales.Orders o
JOIN Membership.Customers c ON o.CustomerId = c.Id
JOIN Sales.OrderItems oi    ON o.Id = oi.OrderId
GROUP BY o.Id, c.FirstName, c.LastName, o.OrderDate, o.Status, o.TotalAmount;
GO

CREATE VIEW Catalog.vw_BookCatalog AS
SELECT
    b.Id, b.Title, b.ISBN, b.Price, b.Pages,
    p.Name          AS Publisher,
    cat.Name        AS Category,
    STRING_AGG(a.FirstName + ' ' + a.LastName, ', ') AS Authors
FROM Catalog.Books b
JOIN Catalog.Publishers p   ON b.PublisherId = p.Id
JOIN Catalog.Categories cat ON b.CategoryId = cat.Id
LEFT JOIN Catalog.BookAuthors ba ON b.Id = ba.BookId
LEFT JOIN Catalog.Authors a      ON ba.AuthorId = a.Id
GROUP BY b.Id, b.Title, b.ISBN, b.Price, b.Pages, p.Name, cat.Name;
GO

-- ══════════════════════════════════════════════════════════════
-- STORED PROCEDURES
-- ══════════════════════════════════════════════════════════════
CREATE PROCEDURE Sales.usp_PlaceOrder
    @CustomerId INT,
    @AddressId  INT,
    @Notes      NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO Sales.Orders (CustomerId, AddressId, Notes)
    VALUES (@CustomerId, @AddressId, @Notes);
    SELECT SCOPE_IDENTITY() AS NewOrderId;
END;
GO

CREATE PROCEDURE Catalog.usp_SearchBooks
    @SearchTerm NVARCHAR(200),
    @CategoryId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.Id, b.Title, b.ISBN, b.Price, p.Name AS Publisher
    FROM Catalog.Books b
    JOIN Catalog.Publishers p ON b.PublisherId = p.Id
    WHERE b.Title LIKE '%' + @SearchTerm + '%'
      AND (@CategoryId IS NULL OR b.CategoryId = @CategoryId);
END;
GO

-- ══════════════════════════════════════════════════════════════
-- SAMPLE DATA
-- ══════════════════════════════════════════════════════════════
INSERT INTO Catalog.Publishers (Name, Country, Website) VALUES
    ('Penguin Random House', 'US', 'https://www.penguinrandomhouse.com'),
    ('HarperCollins', 'US', 'https://www.harpercollins.com'),
    ('O''Reilly Media', 'US', 'https://www.oreilly.com'),
    ('Manning Publications', 'US', 'https://www.manning.com');

INSERT INTO Catalog.Authors (FirstName, LastName, Bio, BornYear) VALUES
    ('Robert', 'Martin', 'Software engineer and author, known as Uncle Bob', 1952),
    ('Martin', 'Fowler', 'Author and speaker on software development', 1963),
    ('Eric', 'Evans', 'Creator of Domain-Driven Design methodology', NULL),
    ('Andrew', 'Hunt', 'Co-author of The Pragmatic Programmer', NULL),
    ('David', 'Thomas', 'Co-author of The Pragmatic Programmer', NULL);

INSERT INTO Catalog.Categories (Name, Description, ParentId) VALUES
    ('Programming', 'Software development books', NULL),
    ('Architecture', 'Software architecture and design', NULL),
    ('C#', '.NET and C# programming', NULL),
    ('Databases', 'Database design and administration', NULL);

-- Link C# as a sub-category of Programming
UPDATE Catalog.Categories SET ParentId = 1 WHERE Name = 'C#';

INSERT INTO Catalog.Books (Title, ISBN, PublisherId, CategoryId, Price, Pages, PublishedAt) VALUES
    ('Clean Code', '978-0132350884', 1, 1, 34.99, 464, '2008-08-01'),
    ('Clean Architecture', '978-0134494166', 1, 2, 29.99, 432, '2017-09-10'),
    ('Domain-Driven Design', '978-0321125217', 2, 2, 54.99, 560, '2003-08-30'),
    ('The Pragmatic Programmer', '978-0135957059', 3, 1, 49.99, 352, '2019-09-13'),
    ('Refactoring', '978-0134757599', 3, 1, 47.99, 448, '2018-11-20'),
    ('C# in Depth', '978-1617294532', 4, 3, 39.99, 528, '2019-03-07');

INSERT INTO Catalog.BookAuthors (BookId, AuthorId) VALUES
    (1, 1), (2, 1), (3, 3), (4, 4), (4, 5), (5, 2), (6, 2);

INSERT INTO Membership.Customers (FirstName, LastName, Email, Phone) VALUES
    ('John', 'Doe', 'john.doe@example.com', '+1-555-0101'),
    ('Jane', 'Smith', 'jane.smith@example.com', '+1-555-0102'),
    ('Alex', 'Johnson', 'alex.j@example.com', NULL);

INSERT INTO Membership.Addresses (CustomerId, Line1, City, State, PostalCode, Country, IsDefault) VALUES
    (1, '123 Main St', 'New York', 'NY', '10001', 'US', 1),
    (2, '456 Oak Ave', 'San Francisco', 'CA', '94102', 'US', 1),
    (3, '789 Pine Rd', 'Chicago', 'IL', '60601', 'US', 1);

INSERT INTO Sales.Orders (CustomerId, AddressId, Status, TotalAmount) VALUES
    (1, 1, 'Delivered', 64.98),
    (2, 2, 'Shipped', 54.99),
    (1, 1, 'Pending', 39.99);

INSERT INTO Sales.OrderItems (OrderId, BookId, Quantity, UnitPrice) VALUES
    (1, 1, 1, 34.99), (1, 2, 1, 29.99),
    (2, 3, 1, 54.99),
    (3, 6, 1, 39.99);

INSERT INTO Sales.Reviews (BookId, CustomerId, Rating, Title, Body) VALUES
    (1, 1, 5, 'Must read', 'Every developer should read this book.'),
    (1, 2, 4, 'Great but dense', 'Very useful but takes time to absorb.'),
    (3, 2, 5, 'Changed how I think about software', 'The strategic patterns are gold.');

PRINT '✅ BookstoreDB created with 11 tables, 2 views, 2 stored procedures, and sample data.';
GO
