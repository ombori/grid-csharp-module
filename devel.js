import fs from 'fs';
import cp from 'child_process';
import IoT from 'azure-iothub';

// TODO: find old images and remove them
// TODO: remove temporary images from server

console.log('Reading ini file');
const data = fs.readFileSync('.env')
  .toString()
  .trim()
  .split('\n')
  .map(line => /^([a-z0-9_-]+)=(.+)$/i.exec(line.trim()))
  .reduce((res, [, key, value]) => ({ ...res, [key]: value }), {});

const reg = IoT.Registry.fromConnectionString(data.IOTHUB_CONNECTION_STRING);

console.log('Reading package.json');
const info = JSON.parse(fs.readFileSync('./package.json').toString());
const name = info.name;
const registry = info['container-registry'];
const [, moduleName] = name.split('.');

let serial = 1;
const id = Math.floor(Math.random() * 10000000);

const oldVersions = [];

console.log('Reading device configuration');
const config = JSON.parse(cp.execSync(`omg app dev twin ${data.DEVICE_NAME}`).toString());

let isDirty = false;
fs.watch('.', { recursive: true }, (op, file) => {
  if (/^.git/.test(file)) return;
  if (/^build/.test(file)) return;
  console.log(op, file);
  isDirty = true;
});

while (true) {
  if (isDirty) {
    try {
      isDirty = false;
      const img = `${registry}/${name}:${id}.${serial}-amd64`;

      console.log("Detecting directory change, rebuilding");
      cp.execSync(`yarn build ${name}@${id}.${serial}`, { stdio: 'inherit' });
      if (isDirty) continue;

      console.log("Uploading image");
      cp.execSync(`docker push ${img}`, { stdio: 'inherit' });
      if (isDirty) continue;

      console.log("Removing old images");
      for (const ver of oldVersions) {
        cp.execSync(`docker rmi ${ver}`, { stdio: 'inherit' });
      }

      oldVersions.push[img];
      serial += 1;

      console.log('Updating device configuration');
      config.modulesContent.$edgeAgent['properties.desired'].modules[moduleName].settings.image = img;
      await reg.applyConfigurationContentOnDevice(data.DEVICE_ID, config);

      console.log('Image uploaded');
    } catch (e) {
      console.error(e.toString());
      isDirty = false;
    }
  }

  await new Promise(resolve => setTimeout(resolve, 1000));
}
