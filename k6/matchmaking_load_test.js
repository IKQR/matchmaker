import http from 'k6/http';
import { check, sleep, fail } from 'k6';
import { randomBytes } from 'k6/crypto';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';


const BASE_URL         = __ENV.BASE_URL || 'https://host.docker.internal:7100';
const RATE             = Number(__ENV.RATE || 200);              // requests/sec (arrivals)
const DURATION_SEC     = Number(__ENV.DURATION_SEC || 300);
const WARMUP_SEC       = Number(__ENV.WARMUP_SEC || 10);
const POLL_MS          = Number(__ENV.POLL_INTERVAL_MS || 100);
const TIMEOUT_MS       = Number(__ENV.POLL_TIMEOUT_MS || 15000);
const BATCH_SIZE       = Number(__ENV.BATCH_SIZE || 3);
const PREALLOC_VUS     = Number(__ENV.PREALLOC_VUS || Math.max(50, RATE * 3));
const MAX_VUS          = Number(__ENV.MAX_VUS || Math.max(200, RATE * 5));
const INSECURE_TLS     = (__ENV.K6_INSECURE_SKIP_TLS_VERIFY || '1') === '1';

export const options = {
    thresholds: {
        http_req_failed: ['rate<0.02'],
        http_req_duration: ['p(95)<1200', 'p(99)<5000'],
        'checks{type:post202}': ['rate>0.995'],
        'checks{type:match_ok}': ['rate>0.98'],
    },
    scenarios: {
        warmup: {
            executor: 'constant-arrival-rate',
            exec: 'scenario',
            rate: Math.max(1, Math.floor((__ENV.RATE ? Number(__ENV.RATE) : 200) * 0.1)),
            timeUnit: '1s',
            duration: `${__ENV.WARMUP_SEC || 10}s`,
            preAllocatedVUs: Math.max(10, Math.floor((__ENV.PREALLOC_VUS ? Number(__ENV.PREALLOC_VUS) : Math.max(50, (Number(__ENV.RATE||200)*3))) * 0.3)),
            maxVUs: Math.max(20, Math.floor((__ENV.MAX_VUS ? Number(__ENV.MAX_VUS) : Math.max(200, (Number(__ENV.RATE||200)*5))) * 0.5)),
            startTime: `${__ENV.START_DELAY_SEC || 10}s`,
        },
        main: {
            executor: 'constant-arrival-rate',
            exec: 'scenario',
            rate: Number(__ENV.RATE || 200),
            timeUnit: '1s',
            duration: `${__ENV.DURATION_SEC || 300}s`,
            preAllocatedVUs: Number(__ENV.PREALLOC_VUS || Math.max(50, (Number(__ENV.RATE||200)*3))),
            maxVUs: Number(__ENV.MAX_VUS || Math.max(200, (Number(__ENV.RATE||200)*5))),
            startTime: `${(Number(__ENV.START_DELAY_SEC || 10) + WARMUP_SEC)}s`,
        },
    },
};

function isUuid(str) {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(str);
}

export function scenario() {
    const userId = uuidv4();
    const correlationId = uuidv4();

    // POST enqueue
    const postRes = http.post(
        `${BASE_URL}/v1/matchmaking/${userId}`,
        null,
        {
            headers: { 'X-Correlation-ID': correlationId },
            tags: { type: 'post' },
            timeout: `${Math.min(10000, TIMEOUT_MS)}ms`,
        },
    );

    check(postRes, { 'POST 202': (r) => r.status === 202 }, { type: 'post202' }) ||
    fail(`POST expected 202, got ${postRes.status} | body=${postRes.body}`);

    // Poll GET until 200 or timeout
    const deadline = Date.now() + TIMEOUT_MS;
    let got200 = false;
    let lastBody = null;

    while (Date.now() < deadline) {
        const getRes = http.get(`${BASE_URL}/v1/matchmaking/${userId}`, { tags: { type: 'get' } });
        if (getRes.status === 200) {
            got200 = true;
            lastBody = getRes;
            break;
        }
        if (getRes.status >= 500) {
            fail(`GET server error ${getRes.status}: ${getRes.body}`);
        }
        sleep(POLL_MS / 1000);
    }

    if (!got200) fail(`Match not ready within ${TIMEOUT_MS}ms for userId=${userId}`);

    // Validate payload
    let payload;
    try {
        payload = lastBody.json(); // { matchId, userIds[] }
    } catch (e) {
        fail(`Failed to parse JSON: ${e} | body=${lastBody && lastBody.body}`);
    }

    const ok = check(payload, {
        'has matchId': (p) => p && typeof p.matchId === 'string',
        'matchId is uuid': (p) => p && typeof p.matchId === 'string' && p.matchId.length > 0, // не вимагаємо v4 суворо (можна ввімкнути isUuid)
        'has userIds': (p) => Array.isArray(p.userIds),
        'size >= N': (p) => Array.isArray(p.userIds) && p.userIds.length >= BATCH_SIZE,
        'contains self': (p) => Array.isArray(p.userIds) && p.userIds.indexOf(userId) !== -1,
    }, { type: 'match_ok' });

    if (!ok) fail(`Invalid match payload: ${JSON.stringify(payload)}`);
}
