import { NgModule } from '@angular/core';
import { Routes, RouterModule } from '@angular/router';
import { LayoutDefaultComponent } from '../layout/default/default.component';
import { AuthGuard } from '@core';
import { FormsComponent } from './components/forms/forms.component';
import { ShopFormsComponent } from './components/shopForms/shopForms.component';


const routes: Routes = [
  { path: '', redirectTo: 'forms', pathMatch: 'full' },
  {
    path: '',
    component: LayoutDefaultComponent,
    canActivate: [AuthGuard],
    children: [
      { path: 'forms', component: FormsComponent, data: { title: '表单列表', permission: 'Pages' } },
      { path: 'shopForms/:id', component: ShopFormsComponent, data: { title: '商家列表', permission: 'Pages' } },

    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class VisitorRoutingModule { }
