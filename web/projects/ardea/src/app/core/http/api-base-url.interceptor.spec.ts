import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, apiBaseUrlInterceptor } from './api-base-url.interceptor';

describe('apiBaseUrlInterceptor', () => {
  function setup(base?: string) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiBaseUrlInterceptor])),
        provideHttpClientTesting(),
        ...(base === undefined ? [] : [{ provide: API_BASE_URL, useValue: base }]),
      ],
    });
    return { http: TestBed.inject(HttpClient), backend: TestBed.inject(HttpTestingController) };
  }

  it('leaves API paths relative in the web app, which shares the origin with the API', () => {
    const { http, backend } = setup();
    http.get('/api/v1/auth/session').subscribe();
    backend.expectOne('/api/v1/auth/session');
  });

  it('puts a configured server address in front of API paths, without a double slash', () => {
    const { http, backend } = setup('https://ardea.example.org/');
    http.get('/api/v1/auth/session').subscribe();
    backend.expectOne('https://ardea.example.org/api/v1/auth/session');
  });

  it('does not touch requests for the app itself, such as translations', () => {
    const { http, backend } = setup('https://ardea.example.org');
    http.get('/i18n/de.json').subscribe();
    backend.expectOne('/i18n/de.json');
  });
});
