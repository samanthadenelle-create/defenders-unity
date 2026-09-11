'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { Readable } = require('node:stream');
const identity = require('../api/_lib/google-identity');
const { persistEmailFingerprint } = require('../api/auth/google-session')._test;
const { createHandler } = require('../api/admin/promo-bind')._test;
const { PAGE } = require('../api/admin/console');
const email = ' Tester@Example.com ';
const player = 'play-' + 'a'.repeat(64);
const fingerprint = identity.deriveEmailHmac(email, 'test-key');
const env = { ADMIN_DASH_KEY:'read-test', ADMIN_OPS_KEY:'write-test', GOOGLE_IDENTITY_KEY:'test-key', DATABASE_URL:'unused' };
function database(rows) {
    const calls = [];
    const sql = async (strings, ...values) => {
        calls.push(typeof strings === 'string'
            ? { text:strings, values:values[0], options:values[1] }
            : { text:strings.join('?'), values });
        if (!rows.length) throw new Error('unexpected query');
        const next = rows.shift();
        if (next instanceof Error) throw next;
        return next;
    };
    sql.calls = calls;
    return sql;
}
async function run(rows, options = {}) {
    const sql = database(rows);
    const audit = [];
    const req = options.stream || { complete:true, body:options.body === undefined ? { email, code:'test-code' } : options.body };
    req.method = options.method || 'POST';
    req.headers = options.headers || { 'x-admin-key':env.ADMIN_DASH_KEY, 'x-admin-ops-key':env.ADMIN_OPS_KEY };
    const out = { headers:{} };
    const res = { setHeader(k,v){ out.headers[k]=v; }, status(s){out.status=s;return this;}, json(b){out.body=b;return this;} };
    await createHandler({ env:options.env || env, connect:()=>sql, audit:async (...args)=>audit.push(args.slice(1)) })(req,res);
    const publicEvidence = JSON.stringify({out,audit});
    for (const secret of [email,email.trim(),email.trim().toLowerCase(),player,fingerprint]) assert.ok(!publicEvidence.includes(secret), 'response/audit must not disclose input or identity');
    assert.ok(!JSON.stringify(sql.calls).includes(email), 'raw email must not enter SQL');
    return { ...out, sql, audit };
}
test('fingerprint is exact keyed SHA256 over lowercased trimmed email', () => {
    assert.equal(fingerprint, crypto.createHmac('sha256','test-key').update('tester@example.com').digest('hex'));
    assert.equal(fingerprint, identity.deriveEmailHmac('tester@example.com','test-key'));
    assert.notEqual(fingerprint, identity.deriveEmailHmac(email,'other-key'));
    assert.throws(()=>identity.deriveEmailHmac(email,''));
    assert.throws(()=>identity.deriveEmailHmac('invalid','test-key'));
});
test('verified claim persists fingerprint against the resolved (possibly pinned) player', async () => {
    const sql = database([[]]);
    assert.equal(await persistEmailFingerprint(sql,player,{email,email_verified:true},env),true);
    assert.deepEqual(sql.calls[0].values,[player,fingerprint]);
    assert.match(sql.calls[0].text,/ON CONFLICT \(player_id\) DO UPDATE SET email_hmac = EXCLUDED.email_hmac/);
});
for (const [label,claims] of [['false',{email,email_verified:false}],['string true',{email,email_verified:'true'}],['absent',{}]]) {
    test('unverified/absent claim clears stale lookup: '+label, async () => {
        const sql = database([[]]);
        await persistEmailFingerprint(sql,player,claims,env);
        assert.deepEqual(sql.calls[0].values,[player,null]);
    });
}
test('missing migration does not fail sign-in metadata; driver messages cannot leak email', async () => {
    const logs=[];const saved=console.warn;console.warn=(...args)=>logs.push(args.join(' '));
    try {
        assert.equal(await persistEmailFingerprint(database([new Error(email)]),player,{email,email_verified:true},env),false);
        assert.match(logs.join('\n'),/migration 0027/);
        assert.ok(!logs.join('\n').includes(email));
    } finally {console.warn=saved;}
});
test('lookup miss refuses before touching promo', async () => {const r=await run([[]]);assert.equal(r.body.error,'NO_MATCH');assert.equal(r.sql.calls.length,1);});
test('multiple identities refuse rather than picking the first', async () => {const r=await run([[{player_id:player},{player_id:'play-'+'b'.repeat(64)}]]);assert.equal(r.body.error,'AMBIGUOUS_MATCH');assert.equal(r.sql.calls.length,1);});
for (const [name,codeRows,error] of [
    ['missing code',[],'CODE_NOT_FOUND'],
    ['inactive code',[{active:false,bound_wallet:null}],'CODE_INACTIVE'],
    ['bound elsewhere',[{active:true,bound_wallet:'other'}],'ALREADY_BOUND_ELSEWHERE'],
    ['concurrent state change',[{active:true,bound_wallet:null}],'CODE_CHANGED_RETRY']
]) test(name+' refuses in words without leaking identity', async()=>{const r=await run([[{player_id:player}],[],codeRows]);assert.equal(r.status,200);assert.equal(r.body.error,error);});
test('binding is conditional at UPDATE and same-player retries are allowed', async()=>{
    const r=await run([[{player_id:player}],[{code:'TEST-CODE'}]]);
    assert.deepEqual(r.body,{success:true,bound:true});
    const q=r.sql.calls[1];assert.match(q.text,/active = TRUE/);assert.match(q.text,/bound_wallet IS NULL OR bound_wallet =/);
    assert.deepEqual(q.values,[player,'TEST-CODE',player]);
    assert.equal(r.headers['Cache-Control'],'no-store');
    assert.ok(!Object.keys(r.headers).some(k=>/access-control/i.test(k)));
});
for (const [label,options,error] of [
    ['GET',{method:'GET'},'METHOD_NOT_ALLOWED'],
    ['missing read key',{headers:{}},'UNAUTHORIZED'],
    ['missing write key',{headers:{'x-admin-key':env.ADMIN_DASH_KEY}},'OPS_UNAUTHORIZED'],
    ['unconfigured writes',{env:{...env,ADMIN_OPS_KEY:''}},'OPS_WRITE_NOT_CONFIGURED'],
    ['unconfigured HMAC',{env:{...env,GOOGLE_IDENTITY_KEY:''}},'GOOGLE_IDENTITY_UNCONFIGURED'],
    ['invalid email',{body:{email:'oops',code:'TEST'}},'EMAIL_INVALID'],
    ['invalid code',{body:{email,code:'<script>'}},'CODE_INVALID'],
    ['array body',{body:[]},'BAD_BODY'],
    ['oversize parsed body',{body:{email,code:'TEST',unused:'x'.repeat(5000)}},'BAD_BODY']
]) test(label+' fails before database lookup',async()=>{const r=await run([],options);assert.equal(r.body.error,error);assert.equal(r.sql.calls.length,0);});
test('raw stream path binds with same contract',async()=>{const r=await run([[{player_id:player}],[{}]],{stream:Readable.from([Buffer.from(JSON.stringify({email,code:'TEST'}))])});assert.equal(r.body.success,true);});
test('database failure exposes only stable error',async()=>{const r=await run([new Error(email)]);assert.equal(r.status,500);assert.equal(r.body.error,'LOOKUP_UNAVAILABLE');});
test('console clears email input and never renders response identity, even if server supplies it',async()=>{
    const section=PAGE.slice(PAGE.indexOf("    if (e.target.id === 'pbind'){"),PAGE.indexOf("    if (e.target.id === 'pcreate'){"));
    const elements={pbemail:{value:email},pbcode:{value:'TEST'},pbresult:{textContent:''}};
    const button={id:'pbind',disabled:false};
    let sent;
    const execute=new Function('e','$','postOps',"var OPS_KEY='test';"+section);
    execute({target:button},id=>elements[id],payload=>{sent={...payload};return Promise.resolve({body:{success:true,email,player_id:player}});});
    assert.equal(elements.pbemail.value,'');assert.equal(button.disabled,true);assert.equal(sent.email,email);
    await Promise.resolve();
    assert.equal(elements.pbresult.textContent,'Bound. The Google player can now redeem this code.');
    assert.equal(button.disabled,false);
});
test('real Google token verification admits signed claims and refuses tampered email', async()=>{
    const { publicKey, privateKey } = crypto.generateKeyPairSync('rsa',{modulusLength:2048});
    const jwk = {...publicKey.export({format:'jwk'}),kid:'test-signing-key',alg:'RS256'};
    const header = Buffer.from(JSON.stringify({alg:'RS256',kid:jwk.kid})).toString('base64url');
    const claims = {iss:'https://accounts.google.com',aud:'test-audience',exp:Math.floor(Date.now()/1000)+300,sub:'subject-test',email,email_verified:true};
    const payload = Buffer.from(JSON.stringify(claims)).toString('base64url');
    const signature = crypto.sign('RSA-SHA256',Buffer.from(header+'.'+payload),privateKey).toString('base64url');
    const options = {env:{GOOGLE_IDENTITY_AUDIENCES:'test-audience'},fetchFn:async()=>({ok:true,headers:{get:()=>null},json:async()=>({keys:[jwk]})})};
    identity._test._resetJwksCache();
    try {
        const verified=await identity.verifyIdToken(header+'.'+payload+'.'+signature,options);
        assert.equal(verified.ok,true);
        const sql=database([[]]);
        await persistEmailFingerprint(sql,player,verified.claims,env);
        assert.deepEqual(sql.calls[0].values,[player,fingerprint]);
        const tampered=Buffer.from(JSON.stringify({...claims,email:'attacker@example.com'})).toString('base64url');
        const refused=await identity.verifyIdToken(header+'.'+tampered+'.'+signature,options);
        assert.equal(refused.ok,false);assert.equal(refused.code,'GOOGLE_TOKEN_BAD_SIGNATURE');
        assert.equal(refused.claims,undefined);
    } finally {identity._test._resetJwksCache();}
});
test('existing promo authentication accepts Play X-Session and refuses another player',async()=>{
    const { authenticatePromoRedeem }=require('../api/_lib/wallet-auth');
    const old=process.env.GOOGLE_IDENTITY_ENABLED;process.env.GOOGLE_IDENTITY_ENABLED='true';
    try {
        const req={headers:{'x-session':'a'.repeat(64)}};
        const accepted=await authenticatePromoRedeem(database([[{wallet:player,revoked:false,expired:false}]]),req,Buffer.from('{}'),player);
        assert.equal(accepted.ok,true);assert.equal(accepted.mode,'google');assert.equal(accepted.unproven,false);
        const refused=await authenticatePromoRedeem(database([[{wallet:'play-'+'b'.repeat(64),revoked:false,expired:false}]]),req,Buffer.from('{}'),player);
        assert.equal(refused.ok,false);assert.equal(refused.code,'AUTH_SESSION_WRONG_WALLET');
    } finally {if(old===undefined)delete process.env.GOOGLE_IDENTITY_ENABLED;else process.env.GOOGLE_IDENTITY_ENABLED=old;}
});

test('optional email lookup aborts at one second and returns despite hung transport', async () => {
    let signal;
    const sql = (_text, _values, options) => { signal=options.fetchOptions.signal; return new Promise(()=>{}); };
    const logs=[];const saved=console.warn;console.warn=(...args)=>logs.push(args.join(' '));
    const started=Date.now();
    try {
        const result=await Promise.race([
            persistEmailFingerprint(sql,player,{email,email_verified:true},env),
            new Promise((_,reject)=>{const watchdog=setTimeout(()=>reject(new Error('lookup hung')),3000);watchdog.unref();}),
        ]);
        assert.equal(result,false);
        assert.equal(signal.aborted,true);
        assert.ok(Date.now()-started < 2500,'optional metadata must release the sign-in path');
        assert.match(logs.join('\n'),/timed out; sign-in continues/);
        for(const secret of [email,player,fingerprint]) assert.ok(!logs.join('\n').includes(secret));
    } finally {console.warn=saved;}
});
