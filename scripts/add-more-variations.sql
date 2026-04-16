-- ============ DEV ============
USE SchemaDoc_Dev;
GO

-- Add a new column with a default (tests DEFAULT constraint handling on drop)
IF COL_LENGTH('app.Users', 'LoginAttempts') IS NULL
    ALTER TABLE app.Users ADD LoginAttempts INT NOT NULL CONSTRAINT DF_Users_LoginAttempts DEFAULT 0;

-- Widen a column type (NVARCHAR(200) → NVARCHAR(300))
IF EXISTS (SELECT 1 FROM sys.columns c
           JOIN sys.tables t ON c.object_id = t.object_id
           WHERE t.name = 'Projects' AND c.name = 'Name' AND c.max_length = 400)  -- 200 chars = 400 bytes
    ALTER TABLE app.Projects ALTER COLUMN Name NVARCHAR(300) NOT NULL;

-- Add a second check constraint
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Users_Email_NotEmpty')
    ALTER TABLE app.Users ADD CONSTRAINT CK_Users_Email_NotEmpty CHECK (LEN(Email) > 0);

-- Create a table with composite PK (for testing composite PK scenarios)
IF OBJECT_ID('app.UserRoles', 'U') IS NULL
BEGIN
    CREATE TABLE app.UserRoles (
        UserId INT NOT NULL,
        RoleName NVARCHAR(50) NOT NULL,
        GrantedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleName),
        CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES app.Users(Id) ON DELETE CASCADE
    );
END;
GO

-- ============ STAGING ============
USE SchemaDoc_Staging;
GO

-- Only a small widening (not to Dev's size)
IF EXISTS (SELECT 1 FROM sys.columns c
           JOIN sys.tables t ON c.object_id = t.object_id
           WHERE t.name = 'Projects' AND c.name = 'Name' AND c.max_length = 400)
    ALTER TABLE app.Projects ALTER COLUMN Name NVARCHAR(250) NOT NULL;

-- Add a different check constraint (not in Dev, not in Prod)
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Tasks_Status_Values')
    ALTER TABLE app.Tasks ADD CONSTRAINT CK_Tasks_Status_Values CHECK (Status IN ('Open', 'InProgress', 'Done', 'Cancelled'));

-- Make a column nullable that was NOT NULL in Dev/Prod
IF EXISTS (SELECT 1 FROM sys.columns c
           JOIN sys.tables t ON c.object_id = t.object_id
           WHERE t.name = 'Users' AND c.name = 'Username' AND c.is_nullable = 0)
    ALTER TABLE app.Users ALTER COLUMN Username NVARCHAR(100) NULL;
GO

-- ============ PROD ============
USE SchemaDoc_Prod;
GO

-- Prod stays minimal, but add an index so there's something to compare
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Tasks_Status_Prod')
    CREATE INDEX IX_Tasks_Status_Prod ON app.Tasks(Status);
GO

PRINT 'Schema variations applied to all three DBs';
