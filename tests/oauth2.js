import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    constant_load: {
      executor: 'constant-vus',
      vus: 50,
      duration: '30s',
    },
    ramp_up: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 200 },
        { duration: '30s', target: 200 },
        { duration: '30s', target: 0 },
      ],
      startTime: '35s',
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const res = http.get('http://host.docker.internal:8080/trigger');

  check(res, {
    'status is 200': (r) => r.status === 200,
    'authenticated_client is service-a': (r) => {
      try {
        return JSON.parse(r.body).authenticated_client === 'service-a';
      } catch {
        return false;
      }
    },
  });

  sleep(0.5);
}