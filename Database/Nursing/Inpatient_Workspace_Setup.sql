/* Run in the Danphe database before deploying the inpatient nursing workspace.
   Adds an append-only care timeline; existing nursing favorites remain personal lists.
   Times use server local time consistently with existing Danphe records. */
IF OBJECT_ID('dbo.NUR_TXN_CareEvent','U') IS NULL
BEGIN
 CREATE TABLE dbo.NUR_TXN_CareEvent (
  CareEventId INT IDENTITY PRIMARY KEY,
  PatientVisitId INT NOT NULL,
  WardId INT NOT NULL,
  Kind VARCHAR(20) NOT NULL,
  Category NVARCHAR(60) NOT NULL,
  Priority VARCHAR(10) NOT NULL,
  Body NVARCHAR(4000) NOT NULL,
  DueOn DATETIME NULL,
  RecipientId INT NULL,
  CreatedBy INT NOT NULL,
  CreatedByName NVARCHAR(100) NOT NULL,
  CreatedOn DATETIME NOT NULL DEFAULT GETDATE(),
  ClosedBy INT NULL,
  ClosedOn DATETIME NULL,
  CONSTRAINT CK_NUR_CareKind CHECK (Kind IN ('Task','Handover','Message','Risk')),
  CONSTRAINT CK_NUR_CarePriority CHECK (Priority IN ('Routine','Urgent')),
  CONSTRAINT FK_NUR_CareVisit FOREIGN KEY (PatientVisitId) REFERENCES PAT_PatientVisits(PatientVisitId)
 );
 CREATE INDEX IX_NUR_CareVisit ON dbo.NUR_TXN_CareEvent(PatientVisitId,CreatedOn DESC);
 CREATE INDEX IX_NUR_CareWard ON dbo.NUR_TXN_CareEvent(WardId,ClosedOn) INCLUDE (Kind,DueOn);
END;
GO
