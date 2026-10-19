/* Run with NODE_PATH pointing at TypeScript 3.1.6, Angular compiler 7 and moment 2.24.
   Exercises actual component methods without constructing hospital service dependencies. */
const fs=require('fs'),vm=require('vm'),assert=require('assert');
const ts=require('typescript'),angular=require('@angular/compiler'),moment=require('moment');
const root='Code/Websites/DanpheEMR/wwwroot/DanpheApp/src/app/';
const file=root+'nursing/department/nursing-inpatient.component.ts';
const source=fs.readFileSync(file,'utf8');
const result=ts.transpileModule(source,{compilerOptions:{target:ts.ScriptTarget.ES5,module:ts.ModuleKind.CommonJS,experimentalDecorators:true},reportDiagnostics:true});
assert.equal((result.diagnostics||[]).length,0,'TypeScript 3.1 syntax diagnostics');
const exportsObject={};
vm.runInNewContext(result.outputText,{exports:exportsObject,require:name=>name.indexOf('moment')>=0?moment:
 name==='@angular/core'?{Component:()=>()=>{}}:new Proxy({},{get:()=>class {}}),console});
const c=Object.create(exportsObject.NursingInPatientComponent.prototype);
Object.assign(c,{careSearch:'',careFilter:'all',careSort:'priority',showFavPatGrid:false,IsReceiveFeatureEnabled:true,
 ipdList:[{PatientVisitId:1,Name:'Abdi',PatientCode:'P001',AdmittingDoctorName:'Dr Hana',BedInformation:{BedCode:'B10'}},
 {PatientVisitId:2,Name:'Beka',PatientCode:'P002',BedInformation:{BedCode:'B2',Action:'admission'}}],
 FavoritePatients:[],careSummary:[{PatientVisitId:1,OpenTasks:2,OverdueTasks:1,ActiveRisks:0,UrgentMessages:0}],
 securityService:{GetLoggedInUser:()=>({EmployeeId:8})}});
assert.equal(c.carePatients[0].PatientVisitId,1,'overdue patients sort first');
c.careSort='bed';assert.equal(c.carePatients[0].PatientVisitId,2,'natural bed sort');
c.careSearch='hana';assert.equal(c.carePatients.length,1,'doctor search');
c.careSearch='p002';assert.equal(c.carePatients[0].Name,'Beka','identifier search');
c.careSearch='';c.showFavPatGrid=true;assert.equal(c.carePatients.length,0,'empty my-patients list never falls back to all');
c.showFavPatGrid=false;c.careFilter='overdue';assert.equal(c.carePatients.length,1,'overdue filter');
c.careFilter='receive';assert.equal(c.carePatients[0].PatientVisitId,2,'reception filter');
assert(c.CareCanClose({Kind:'Message',RecipientId:8}));
assert(!c.CareCanClose({Kind:'Message',RecipientId:9}),'only recipient acknowledges');
assert(!c.CareCanClose({Kind:'Task',ClosedOn:'2026-10-01'}));
assert(c.CareOverdue({Kind:'Task',DueOn:'2000-01-01'}));
assert(!c.CareOverdue({Kind:'Task',DueOn:'2000-01-01',ClosedOn:'2026-10-01'}));
assert.equal(c.CareDays({AdmittedDate:'2026-99-99'}),'—');
let request;
c.careSelected={PatientVisitId:1};c.careSaving=false;c.careLoading=false;c.careError='';
c.careKind='Message';c.careSbar={situation:'Pain',background:'Post-op',assessment:'Observed',recommendation:'Please review'};
c.carePriority='Urgent';c.careReplyToId=0;c.securityService.getActiveWard=()=>({WardId:4});
c.PostCare=(path,body)=>request={path,body};c.SaveCare();assert.equal(request.body.Category,'SBAR');assert(request.body.Body.includes('Recommendation: Please review'));
c.careSaving=true;request=null;c.SaveCare();assert.equal(request,null,'no double submit');
for(const f of [file,root+'shared/inpatient-care-messages/inpatient-care-messages.component.ts',root+'core/notifications/notification.component.ts']) {
 const code=fs.readFileSync(f,'utf8');
 const r=ts.transpileModule(code,{compilerOptions:{experimentalDecorators:true,target:ts.ScriptTarget.ES5},reportDiagnostics:true});
 assert.equal((r.diagnostics||[]).length,0,f);
}
for(const f of ['nursing/department/nursing-inpatient.html','doctors/patient/patient-overview.html','clinical-new/clinical-overview-wrapper/clinical-overview-wrapper.component.html']) {
 const parsed=angular.parseTemplate(fs.readFileSync(root+f,'utf8'),f);
 assert(!parsed.errors || !parsed.errors.length,JSON.stringify(parsed.errors));
}
const inbox=fs.readFileSync(root+'shared/inpatient-care-messages/inpatient-care-messages.component.ts','utf8').match(/template: `([\s\S]*?)`,/)[1];
const parsed=angular.parseTemplate(inbox,'inbox');assert(!parsed.errors || !parsed.errors.length,JSON.stringify(parsed.errors));
console.log('PASS: patient scoping, search, priority / bed sort, task state, recipient acknowledgement, SBAR payload, double-submit guard, TS 3.1 syntax and Angular 7 templates');
