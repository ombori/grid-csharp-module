import fs from 'fs';
import cp from 'child_process';
import IoT from 'azure-iothub';

const data = fs.readFileSync('.env')
  .toString()
  .trim()
  .split('\n')
  .map(line => /^([a-z0-9_-]+)=(.+)$/i.exec(line.trim()))
  .reduce((res, [, key, value]) => ({ ...res, [key]: value }), {});

const info = JSON.parse(fs.readFileSync('./package.json').toString());

const name = info.name;
const registry = info['container-registry'];

const [, moduleName] = name.split('.');

const serial = 1;
const id = Math.floor(Math.random() * 10000000);

const oldVersions = [];

const config = JSON.parse(cp.execSync(`omg app dev twin ${data.DEVICE_NAME}`).toString());

const img = `${registry}/${name}:${id}.${serial}-amd64`;
oldVersions.push[img];
cp.execSync(`yarn build ${name}@${id}.${serial}`, { stdio: 'inherit' });
cp.execSync(`docker push ${img}`, { stdio: 'inherit' });

// update device configutation
config.modulesContent.$edgeAgent['properties.desired'].modules[moduleName].settings.image = img;

const reg = IoT.Registry.fromConnectionString(data.IOTHUB_CONNECTION_STRING);
await reg.applyConfigurationContentOnDevice(data.DEVICE_ID, config);

for (const ver of oldVersions) {
  cp.execSync(`docker rmi ${ver}`, { stdio: 'inherit' });
}
