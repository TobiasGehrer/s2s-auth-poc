import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    constant_load: {
      executor: 'constant-vus',
      vus: 50,
      duration: '120s',
    },
  },
  thresholds: {
    // Hier kein Threshold, um das Verhalten beim Ausfall zu beobachten
  },
};

export default function () {
  const res = http.get('http://host.docker.internal:8080/trigger', {
    timeout: '5s',
  });

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  sleep(0.5);
}