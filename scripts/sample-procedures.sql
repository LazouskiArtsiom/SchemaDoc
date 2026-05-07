-- ════════════════════════════════════════════════════════════════════
-- Sample stored procedures, functions, and a computed column for
-- HRSystemDB. Used to demonstrate SchemaDoc's procedure-detail page,
-- dependency tracking, and PDF rendering of procs/functions/computed
-- columns.
--
-- Idempotent: drops + recreates each object. Safe to re-run.
-- ════════════════════════════════════════════════════════════════════
USE HRSystemDB;
GO

-- Required for ALTER TABLE ... ADD <computed col> PERSISTED and for
-- procedures/functions that reference computed columns. sqlcmd defaults to OFF.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ── Computed column on Employees (FullName) ─────────────────────────
IF EXISTS (
    SELECT 1 FROM sys.computed_columns cc
    JOIN sys.tables t ON cc.object_id = t.object_id
    WHERE t.name = 'Employees' AND cc.name = 'FullName'
)
    ALTER TABLE dbo.Employees DROP COLUMN FullName;
GO
ALTER TABLE dbo.Employees
    ADD FullName AS (FirstName + N' ' + LastName) PERSISTED;
GO

-- ── Function: scalar return (employee full name + tenure years) ─────
IF OBJECT_ID('dbo.fn_GetEmployeeTenureYears', 'FN') IS NOT NULL
    DROP FUNCTION dbo.fn_GetEmployeeTenureYears;
GO
CREATE FUNCTION dbo.fn_GetEmployeeTenureYears (@EmployeeId INT)
RETURNS INT
AS
BEGIN
    DECLARE @years INT;
    SELECT @years = DATEDIFF(YEAR, e.HireDate, GETDATE())
    FROM dbo.Employees e
    WHERE e.Id = @EmployeeId;
    RETURN ISNULL(@years, 0);
END
GO

-- ── Function: inline table-valued (employees in a department) ───────
IF OBJECT_ID('dbo.fn_EmployeesInDepartment', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_EmployeesInDepartment;
GO
CREATE FUNCTION dbo.fn_EmployeesInDepartment (@DepartmentId INT)
RETURNS TABLE
AS
RETURN
    SELECT  e.Id,
            e.FullName,
            e.Email,
            e.Salary,
            j.Title AS CurrentTitle
    FROM    dbo.Employees e
    LEFT JOIN dbo.EmployeeJobHistory h
            ON h.EmployeeId = e.Id AND h.EndDate IS NULL
    LEFT JOIN dbo.JobTitles j
            ON j.Id = h.JobTitleId
    WHERE   e.DepartmentId = @DepartmentId
        AND e.IsActive = 1;
GO

-- ── SP: list employees with manager + department + current title ────
IF OBJECT_ID('dbo.sp_GetEmployeesByDepartment', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_GetEmployeesByDepartment;
GO
CREATE PROCEDURE dbo.sp_GetEmployeesByDepartment
    @DepartmentId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  e.Id,
            e.FullName,
            e.Email,
            e.HireDate,
            e.Salary,
            d.Name      AS DepartmentName,
            j.Title     AS CurrentTitle,
            mgr.FullName AS ManagerName,
            dbo.fn_GetEmployeeTenureYears(e.Id) AS TenureYears
    FROM    dbo.Employees e
    JOIN    dbo.Departments d
            ON d.Id = e.DepartmentId
    LEFT JOIN dbo.Employees mgr
            ON mgr.Id = e.ManagerId
    LEFT JOIN dbo.EmployeeJobHistory h
            ON h.EmployeeId = e.Id AND h.EndDate IS NULL
    LEFT JOIN dbo.JobTitles j
            ON j.Id = h.JobTitleId
    WHERE   e.DepartmentId = @DepartmentId
        AND e.IsActive = 1
    ORDER BY e.LastName, e.FirstName;
END
GO

-- ── SP: promote employee (salary update + history insert in tx) ─────
IF OBJECT_ID('dbo.sp_PromoteEmployee', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_PromoteEmployee;
GO
CREATE PROCEDURE dbo.sp_PromoteEmployee
    @EmployeeId    INT,
    @NewJobTitleId INT,
    @NewSalary     DECIMAL(10,2),
    @EffectiveDate DATE = NULL,
    @PreviousSalary DECIMAL(10,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @EffectiveDate = ISNULL(@EffectiveDate, CAST(GETDATE() AS DATE));

    BEGIN TRAN;

    SELECT @PreviousSalary = Salary
    FROM   dbo.Employees
    WHERE  Id = @EmployeeId;

    -- Close out the current title's history row, if any
    UPDATE dbo.EmployeeJobHistory
       SET EndDate = DATEADD(DAY, -1, @EffectiveDate)
     WHERE EmployeeId = @EmployeeId
       AND EndDate IS NULL;

    -- Insert the new title's history row
    INSERT INTO dbo.EmployeeJobHistory (EmployeeId, JobTitleId, StartDate, Salary)
    VALUES (@EmployeeId, @NewJobTitleId, @EffectiveDate, @NewSalary);

    -- Update the employee's current salary
    UPDATE dbo.Employees
       SET Salary = @NewSalary
     WHERE Id = @EmployeeId;

    COMMIT TRAN;
END
GO

-- ── SP: pending leave requests with approver + employee details ─────
IF OBJECT_ID('dbo.sp_GetPendingLeaveRequests', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_GetPendingLeaveRequests;
GO
CREATE PROCEDURE dbo.sp_GetPendingLeaveRequests
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  lr.Id,
            lr.LeaveType,
            lr.StartDate,
            lr.EndDate,
            lr.Notes,
            e.FullName  AS EmployeeName,
            e.Email     AS EmployeeEmail,
            d.Name      AS DepartmentName,
            approver.FullName AS ApproverName
    FROM    dbo.LeaveRequests lr
    JOIN    dbo.Employees e
            ON e.Id = lr.EmployeeId
    LEFT JOIN dbo.Departments d
            ON d.Id = e.DepartmentId
    LEFT JOIN dbo.Employees approver
            ON approver.Id = lr.ApprovedById
    WHERE   lr.Status = N'Pending'
    ORDER BY lr.StartDate;
END
GO

PRINT 'Sample procedures, functions, and computed column installed.';
