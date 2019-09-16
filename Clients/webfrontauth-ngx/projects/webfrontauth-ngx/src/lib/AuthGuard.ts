import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivate, RouterStateSnapshot, Router, CanActivateChild } from '@angular/router';
import { AuthService, AuthLevel } from '@signature/webfrontauth';
import { AuthServiceClientConfiguration } from './AuthServiceClientConfiguration';
import { NgxAuthModule } from './NgxAuthModule';

@Injectable({ providedIn: NgxAuthModule })
export class AuthGuard implements CanActivate, CanActivateChild {

  constructor(
    private router: Router,
    private authService: AuthService,
    private authConfig: AuthServiceClientConfiguration
  ) {
  }

  public canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean {
    if (this.authService.authenticationInfo.level >= AuthLevel.Normal) { return true; }
    this.router.navigate([this.authConfig.loginPath], { queryParams: { returnUrl: state.url } });
    return false;
  }

  public canActivateChild(childRoute: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean {
    return this.canActivate(childRoute, state);
  }
}
