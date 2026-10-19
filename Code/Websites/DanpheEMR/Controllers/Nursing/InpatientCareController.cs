using DanpheEMR.Core.Configuration;
using DanpheEMR.Security;
using DanpheEMR.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace DanpheEMR.Controllers
{
    // All writes are attributed to the authenticated employee; clients cannot choose recipients.
    public class InpatientCareController : CommonController
    {
        public InpatientCareController(IOptions<MyConfiguration> config) : base(config) { }

        private RbacUser UserForCare()
        {
            var user = HttpContext.Session.Get<RbacUser>("currentuser");
            if (user == null) throw new UnauthorizedAccessException("Sign in to use inpatient care.");
            return user;
        }
        private bool IsNurse(RbacUser user)
        {
            return RBAC.UserHasPermission(user.UserId, "nursing-ip-summary-view");
        }
        private int? VerifyVisit(SqlConnection db, int visitId, int wardId, RbacUser user, bool nurseOnly = false)
        {
            using (var cmd = new SqlCommand(@"SELECT a.AdmittingDoctorId FROM ADT_PatientAdmission a
                WHERE a.PatientVisitId=@visit AND a.AdmissionStatus='admitted'
                AND EXISTS (SELECT 1 FROM ADT_TXN_PatientBedInfo b WHERE b.PatientVisitId=a.PatientVisitId
                   AND b.WardId=@ward AND b.IsActive=1 AND b.EndedOn IS NULL)", db))
            {
                cmd.Parameters.AddWithValue("@visit", visitId);
                cmd.Parameters.AddWithValue("@ward", wardId);
                using (var row = cmd.ExecuteReader())
                {
                    if (!row.Read()) throw new InvalidOperationException("Patient is no longer admitted in this ward. Refresh the list.");
                    int? doctor = row.IsDBNull(0) ? (int?)null : row.GetInt32(0);
                    if (!IsNurse(user) && (nurseOnly || doctor != user.EmployeeId))
                        throw new UnauthorizedAccessException("Inpatient nursing permission or treating doctor access required.");
                    return doctor;
                }
            }
        }
        [HttpGet, Route("Messages")]
        public IActionResult Messages(int patientVisitId)
        {
            return InvokeHttpGetFunction(() => {
                var user=UserForCare();
                using(var db=new SqlConnection(connString))
                {
                    db.Open(); int ward;
                    using(var cmd=new SqlCommand(@"SELECT TOP (1) b.WardId FROM ADT_TXN_PatientBedInfo b
                      JOIN ADT_PatientAdmission a ON a.PatientVisitId=b.PatientVisitId
                      WHERE b.PatientVisitId=@visit AND b.IsActive=1 AND b.EndedOn IS NULL
                      AND a.AdmissionStatus='admitted' ORDER BY b.PatientBedInfoId DESC",db))
                    {
                        cmd.Parameters.AddWithValue("@visit",patientVisitId); var result=cmd.ExecuteScalar();
                        if(result==null) return (object)new {Enabled=false};
                        ward=Convert.ToInt32(result);
                    }
                    VerifyVisit(db,patientVisitId,ward,user);
                    using(var cmd=new SqlCommand(@"SELECT TOP (100) * FROM NUR_TXN_CareEvent
                      WHERE PatientVisitId=@visit AND Kind='Message' ORDER BY CreatedOn DESC,CareEventId DESC",db))
                    {
                        cmd.Parameters.AddWithValue("@visit",patientVisitId);
                        return (object)new {Enabled=true,WardId=ward,Messages=ReadRows(cmd)};
                    }
                }
            });
        }
        [HttpGet, Route("Timeline")]
        public IActionResult Timeline(int patientVisitId, int wardId)
        {
            return InvokeHttpGetFunction(() => {
                var user = UserForCare();
                using (var db = new SqlConnection(connString))
                {
                    db.Open(); VerifyVisit(db, patientVisitId, wardId, user);
                    using (var cmd = new SqlCommand(@"SELECT TOP (500) * FROM NUR_TXN_CareEvent
                       WHERE PatientVisitId=@visit ORDER BY CreatedOn DESC,CareEventId DESC", db))
                    {
                        cmd.Parameters.AddWithValue("@visit", patientVisitId);
                        return ReadRows(cmd);
                    }
                }
            });
        }
        [HttpGet, Route("WardSummary")]
        public IActionResult WardSummary(int wardId)
        {
            return InvokeHttpGetFunction(() => {
                var user = UserForCare();
                if (!IsNurse(user)) throw new UnauthorizedAccessException("Inpatient nursing permission required.");
                using (var db = new SqlConnection(connString))
                {
                    db.Open();
                    using (var cmd = new SqlCommand(@"SELECT e.PatientVisitId,
                       SUM(CASE WHEN e.Kind='Task' THEN 1 ELSE 0 END) OpenTasks,
                       SUM(CASE WHEN e.Kind='Task' AND e.DueOn<GETDATE() THEN 1 ELSE 0 END) OverdueTasks,
                       SUM(CASE WHEN e.Kind='Risk' THEN 1 ELSE 0 END) ActiveRisks,
                       SUM(CASE WHEN e.Kind='Message' AND e.Priority='Urgent' THEN 1 ELSE 0 END) UrgentMessages
                       FROM NUR_TXN_CareEvent e WHERE e.ClosedOn IS NULL AND EXISTS
                       (SELECT 1 FROM ADT_TXN_PatientBedInfo b JOIN ADT_PatientAdmission a ON a.PatientVisitId=b.PatientVisitId
                         WHERE b.PatientVisitId=e.PatientVisitId AND b.WardId=@ward AND b.IsActive=1
                         AND b.EndedOn IS NULL AND a.AdmissionStatus='admitted') GROUP BY e.PatientVisitId", db))
                    {
                        cmd.Parameters.AddWithValue("@ward", wardId); return ReadRows(cmd);
                    }
                }
            });
        }
        [HttpPost, Route("Record")]
        public IActionResult Record([FromBody] CareRecord input)
        {
            return InvokeHttpGetFunction(() => {
                var user = UserForCare();
                if (input == null || string.IsNullOrWhiteSpace(input.Body) || input.Body.Length>4000
                    || string.IsNullOrWhiteSpace(input.Category) || input.Category.Length>60
                    || !new List<string>{"Task","Handover","Message","Risk"}.Contains(input.Kind)
                    || !new List<string>{"Routine","Urgent"}.Contains(input.Priority))
                    throw new ArgumentException("Valid type, category and text (up to 4000 characters) are required.");
                if (input.Kind != "Task" && input.DueOn.HasValue) throw new ArgumentException("Only tasks have due dates.");
                using (var db = new SqlConnection(connString))
                {
                    db.Open(); var doctor = VerifyVisit(db,input.PatientVisitId,input.WardId,user,input.Kind!="Message");
                    if (input.Kind=="Message" && !doctor.HasValue)
                        throw new InvalidOperationException("No admitting doctor assigned. Contact the ward lead.");
                    using (var tx = db.BeginTransaction())
                    {
                        using (var cmd = new SqlCommand(@"INSERT INTO NUR_TXN_CareEvent
                          (PatientVisitId,WardId,Kind,Category,Priority,Body,DueOn,RecipientId,CreatedBy,CreatedByName)
                          OUTPUT INSERTED.CareEventId VALUES (@visit,@ward,@kind,@category,@priority,@body,@due,@recipient,@by,@name)",db,tx))
                        {
                            cmd.Parameters.AddWithValue("@visit",input.PatientVisitId); cmd.Parameters.AddWithValue("@ward",input.WardId);
                            cmd.Parameters.AddWithValue("@kind",input.Kind); cmd.Parameters.AddWithValue("@category",input.Category.Trim());
                            cmd.Parameters.AddWithValue("@priority",input.Priority); cmd.Parameters.AddWithValue("@body",input.Body.Trim());
                            cmd.Parameters.AddWithValue("@due",(object)input.DueOn??DBNull.Value);
                            // A reply targets the original sender; never trust an arbitrary employee ID from the browser.
                            int? recipient = input.Kind=="Message" ? doctor : null;
                            if (input.Kind=="Message" && !IsNurse(user))
                            {
                                using (var parent = new SqlCommand("SELECT CreatedBy FROM NUR_TXN_CareEvent WHERE CareEventId=@id AND PatientVisitId=@visit AND Kind='Message' AND RecipientId=@me",db,tx))
                                {
                                    parent.Parameters.AddWithValue("@id",input.ReplyToId); parent.Parameters.AddWithValue("@visit",input.PatientVisitId);
                                    parent.Parameters.AddWithValue("@me",user.EmployeeId); var sender=parent.ExecuteScalar();
                                    if(sender==null) throw new ArgumentException("Select the nurse message to reply to.");
                                    recipient=Convert.ToInt32(sender);
                                }
                            }
                            cmd.Parameters.AddWithValue("@recipient",(object)recipient??DBNull.Value);
                            cmd.Parameters.AddWithValue("@by",user.EmployeeId); cmd.Parameters.AddWithValue("@name",user.UserName);
                            var id=Convert.ToInt32(cmd.ExecuteScalar());
                            if (recipient.HasValue)
                            {
                                using(var notify=new SqlCommand(@"INSERT INTO CORE_Notification
                                  (Notification_ModuleName,Notification_Title,Notification_Details,RecipientId,ParentTableName,
                                   NotificationParentId,IsRead,CreatedOn,IsArchived,RecipientType,Sub_ModuleName)
                                  VALUES ('Nursing',@title,@detail,@recipient,'PAT_PatientVisits',@visit,0,GETDATE(),0,'user','InpatientCare')",db,tx))
                                {
                                    notify.Parameters.AddWithValue("@title",input.Priority+" inpatient nurse message");
                                    notify.Parameters.AddWithValue("@detail","Visit "+input.PatientVisitId+": open the patient overview to review nurse messages. From "+user.UserName);
                                    notify.Parameters.AddWithValue("@recipient",recipient.Value); notify.Parameters.AddWithValue("@visit",input.PatientVisitId);
                                    notify.ExecuteNonQuery();
                                }
                            }
                            tx.Commit(); return id;
                        }
                    }
                }
            });
        }
        [HttpPost, Route("Close")]
        public IActionResult Close([FromBody] CareRecord input)
        {
            return InvokeHttpGetFunction(() => {
                if(input==null) throw new ArgumentException("Record required.");
                var user=UserForCare();
                using(var db=new SqlConnection(connString))
                {
                    db.Open(); VerifyVisit(db,input.PatientVisitId,input.WardId,user);
                    using(var cmd=new SqlCommand(@"UPDATE NUR_TXN_CareEvent SET ClosedOn=GETDATE(),ClosedBy=@me
                      WHERE CareEventId=@id AND PatientVisitId=@visit AND ClosedOn IS NULL
                      AND ((Kind='Message' AND RecipientId=@me) OR (Kind IN ('Task','Risk') AND @nurse=1))",db))
                    {
                        cmd.Parameters.AddWithValue("@id",input.CareEventId);cmd.Parameters.AddWithValue("@visit",input.PatientVisitId);
                        cmd.Parameters.AddWithValue("@me",user.EmployeeId);cmd.Parameters.AddWithValue("@nurse",IsNurse(user));
                        if(cmd.ExecuteNonQuery()!=1) throw new InvalidOperationException("Already closed or you cannot close this record. Refresh.");
                        return true;
                    }
                }
            });
        }
        private static List<Dictionary<string,object>> ReadRows(SqlCommand cmd)
        {
            var rows=new List<Dictionary<string,object>>();
            using(var reader=cmd.ExecuteReader()) while(reader.Read())
            {
                var row=new Dictionary<string,object>();
                for(int i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }
        public class CareRecord
        {
            public int CareEventId {get;set;} public int PatientVisitId {get;set;} public int WardId {get;set;}
            public string Kind {get;set;} public string Category {get;set;} public string Priority {get;set;}
            public string Body {get;set;} public DateTime? DueOn {get;set;} public int ReplyToId {get;set;}
        }
    }
}
