import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync,writeFileSync } from 'node:fs';
import zxing from '@zxing/library';
const { MultiFormatOneDReader: MultiFormatReader,RGBLuminanceSource,HybridBinarizer,BinaryBitmap,BarcodeFormat } = zxing;

const left=['0001101','0011001','0010011','0111101','0100011','0110001','0101111','0111011','0110111','0001011'];
const parity=['LLLLLL','LLGLGG','LLGGLG','LLGGGL','LGLLGG','LGGLLG','LGGGLL','LGLGLG','LGLGGL','LGGLGL'];
const invert=bits=>bits.replace(/[01]/g,c=>c==='0'?'1':'0');
function barcode(code){
 let bits='0000000000101';for(let i=1;i<=6;i++)bits+=parity[code[0]][i-1]==='L'?left[code[i]]:invert([...left[code[i]]].reverse().join(''));
 bits+='01010';for(let i=7;i<13;i++)bits+=invert(left[code[i]]);bits+='1010000000000';
 const width=bits.length*3,height=180,pixels=new Uint8ClampedArray(width*height).fill(255);
 for(let x=0;x<width;x++)if(bits[Math.floor(x/3)]==='1')for(let y=10;y<150;y++)pixels[y*width+x]=0;
 const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}"><rect width="100%" height="100%" fill="white"/>${[...bits].map((bit,i)=>bit==='1'?`<rect x="${i*3}" y="10" width="3" height="140"/>`:'').join('')}<text x="20" y="175" font-size="18">${code}</text></svg>`;
 return {bitmap:new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(pixels,width,height))),svg};
}
test('scanner decodes a book ISBN from EAN-13 bars',()=>{const code='9780671741457';const fixture=barcode(code);const result=new MultiFormatReader().decode(fixture.bitmap);assert.equal(result.getText(),code);assert.equal(result.getBarcodeFormat(),BarcodeFormat.EAN_13);mkdirSync('work/scanner-qa',{recursive:true});writeFileSync('work/scanner-qa/isbn.svg',fixture.svg);});
test('scanner decodes UPC-A retail bars without pretending they are ISBNs',()=>{const result=new MultiFormatReader().decode(barcode('0036000291452').bitmap);assert.equal(result.getText(),'036000291452');assert.equal(result.getBarcodeFormat(),BarcodeFormat.UPC_A);});
test('scanner rejects a barcode with an invalid checksum',()=>{assert.throws(()=>new MultiFormatReader().decode(barcode('9780671706721').bitmap));});
