import { Component, Input, OnChanges } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { SecurityService } from '../../security/shared/security.service';

@Component({
  selector: 'inpatient-care-messages',
  template: `
    <section *ngIf="enabled" class="nurse-messages">
      <h4>Nurse ↔ doctor communication</h4>
      <p>Patient-specific SBAR messages. Use the hospital emergency call process for emergencies.</p>
      <button type="button" (click)="load()" [disabled]="busy">Refresh messages</button>
      <p *ngIf="error" role="alert">{{error}}</p>
      <article *ngFor="let m of messages">
        <strong>{{m.Priority}} · {{m.CreatedByName}} · {{m.CreatedOn | date:'medium'}}</strong>
        <p class="message-body">{{m.Body}}</p>
        <p *ngIf="m.ClosedOn">Acknowledged {{m.ClosedOn | date:'medium'}} by employee {{m.ClosedBy}}</p>
        <span *ngIf="!m.ClosedOn">Awaiting recipient acknowledgement</span>
        <button type="button" *ngIf="m.RecipientId===employeeId && !m.ClosedOn" (click)="act('Close',m)" [disabled]="busy">Acknowledge</button>
        <button type="button" *ngIf="m.RecipientId===employeeId" (click)="replyTo=m.CareEventId" [disabled]="busy">Reply</button>
      </article>
      <p *ngIf="!messages.length">No messages for this admission.</p>
      <form *ngIf="replyTo" (ngSubmit)="act('Record',null)">
        <label>Reply to message #{{replyTo}}<textarea name="reply" [(ngModel)]="reply" required maxlength="4000"></textarea></label>
        <p>Clinical orders must be entered through the prescribing / orders workflow.</p>
        <button type="submit" [disabled]="busy || !reply.trim()">Send reply</button>
        <button type="button" (click)="replyTo=0;reply=''" [disabled]="busy">Cancel</button>
      </form>
    </section>`,
  styles: [`.nurse-messages{border:1px solid #b8d7d5;border-radius:8px;padding:16px;margin:16px 0;background:#f4faf9}.message-body{white-space:pre-wrap;overflow-wrap:anywhere}article{padding:12px 0;border-bottom:1px solid #d6e4e4}button{margin:5px;padding:7px 12px}textarea{display:block;width:100%;min-height:90px}button:focus,textarea:focus{outline:2px solid teal}`]
})
export class InpatientCareMessagesComponent implements OnChanges {
  @Input() visitId: number;
  public enabled=false;
  public messages: any[]=[];
  public error='';
  public busy=false;
  public wardId=0;
  public replyTo=0;
  public reply='';
  public employeeId: number;
  private request=0;
  constructor(private http: HttpClient,private security: SecurityService) {
    this.employeeId=this.security.GetLoggedInUser().EmployeeId;
  }
  ngOnChanges() {this.replyTo=0;this.reply='';this.enabled=false;this.load();}
  load() {
    if(!this.visitId) return;
    const request=++this.request;
    this.http.get<any>('/api/InpatientCare/Messages?patientVisitId='+this.visitId).subscribe(res => {
      if(request!==this.request) return;
      if(res.Status==='OK') {this.enabled=res.Results.Enabled;this.messages=res.Results.Messages || [];this.wardId=res.Results.WardId;this.error='';}
      else {this.error='Unable to load nurse messages. Check access and database setup.';}
    }, () => {if(request===this.request)this.error='Unable to refresh messages.';});
  }
  act(path: string,m: any) {
    if(this.busy) return;
    const visit=this.visitId;
    this.busy=true;this.error='';
    this.http.post<any>('/api/InpatientCare/'+path,{PatientVisitId:visit,WardId:this.wardId,
      CareEventId:m?m.CareEventId:0,ReplyToId:this.replyTo,Kind:'Message',Category:'SBAR reply',Priority:'Routine',Body:this.reply})
      .subscribe(res => {this.busy=false;if(visit!==this.visitId)return;
        if(res.Status==='OK'){this.reply='';this.replyTo=0;this.load();}else this.error='Not saved. '+(res.ErrorMessage || 'Refresh and retry.');
      }, () => {this.busy=false;this.error='Save failed. Refresh messages before retrying.';});
  }
}
